// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Contacts;

namespace Gameplay
{
    public class SceneStreaming : SyncScript
    {
        private Task<Scene> loadingTask;
        private CancellationTokenSource loadCancellation;

        /// <summary>
        /// The loaded scene
        /// </summary>
        [DataMemberIgnore]
        public Scene Instance { get; private set; }

        /// <summary>
        /// The url of the scene to load
        /// </summary>
        public UrlReference<Scene> Url { get; set; }

        /// <summary>
        /// The trigger volume. This should be a static collider: the script makes it a trigger
        /// </summary>
        public CollidableComponent Trigger { get; set; }

        private readonly DepthTrigger depths = new DepthTrigger();

        /// <summary>
        /// The distance a collider has to enter the trigger before the scene starts loading asynchronously
        /// </summary>
        [DefaultValue(0.25f)]
        public float PreLoadDepth { get; set; } = 0.25f;

        /// <summary>
        /// The distance a collider has to enter the trigger before the scene starts loading synchronously
        /// </summary>
        [DefaultValue(0.5f)]
        public float LoadDepth { get; set; } = 0.5f;

        public override void Start()
        {
            if (Trigger != null)
                Trigger.ContactEventHandler = depths;
        }

        public override void Update()
        {
            if (Trigger == null)
                return;

            // The collision matrix of the simulation chooses which collidables touch the trigger volume
            var deepest = depths.Deepest.Count > 0 ? depths.Deepest.Values.Max() : 0f;

            // Is any collidable deep enough for synchronous loading, or else asynchronous loading? Is any inside at all?
            bool shouldLoad = deepest > LoadDepth;
            bool shouldPreLoad = deepest > PreLoadDepth;
            bool shouldUnload = deepest <= 0f;

            if (!shouldUnload)
            {
                // Loading is already in progress, or even finished
                if (loadingTask == null)
                {
                    if (shouldLoad)
                    {
                        // If we should load syncrhonously, just create a completed task and load 
                        Instance = Content.Load(Url);
                        loadingTask = Task.FromResult(Instance);
                    }
                    else if (shouldPreLoad)
                    {
                        loadCancellation = new CancellationTokenSource();

                        var localLoadingTask = loadingTask = Content.LoadAsync(Url);
                        Script.AddTask(async () =>
                        {
                            await loadingTask;

                            // Immediately unload if unload or sync load was triggered in the meantime
                            if (loadCancellation.IsCancellationRequested || loadingTask != localLoadingTask)
                            {
                                Content.Unload(localLoadingTask.Result);
                                loadCancellation = null;

                                // Unloading was triggered
                                if (loadingTask == localLoadingTask)
                                    loadingTask = null;
                            }
                            else
                            {
                                Instance = loadingTask.Result;
                            }
                        });
                    }
                }

                // Once loaded, add it to the scene
                if (Instance != null)
                {
                    Instance.Parent = Entity.Scene;
                }
            }
            else
            {
                // Cancel loading if currently in progress, and reset state
                loadCancellation?.Cancel();
                loadCancellation = null;
                loadingTask = null;

                // Unload if already finished
                if (Instance != null)
                {
                    Content.Unload(Instance);

                    // If we were the last user, detach. Ideally scripts should cooperate differently
                    if (!Content.IsLoaded(Url))
                    {
                        Instance.Parent = null;
                    }

                    Instance = null;
                }
            }
        }

        /// <summary>
        /// A trigger that keeps how deep each collidable inside it is.
        /// </summary>
        private sealed class DepthTrigger : IContactHandler
        {
            public readonly Dictionary<CollidableComponent, float> Deepest = new Dictionary<CollidableComponent, float>();

            public bool NoContactResponse => true;

            void IContactHandler.OnTouching<TManifold>(Contacts<TManifold> contacts)
            {
                var deepest = 0f;
                foreach (var contact in contacts)
                    deepest = Math.Max(deepest, contact.Depth);
                Deepest[contacts.Other] = deepest;
            }

            void IContactHandler.OnStoppedTouching<TManifold>(Contacts<TManifold> contacts) => Deepest.Remove(contacts.Other);
        }
    }
}
