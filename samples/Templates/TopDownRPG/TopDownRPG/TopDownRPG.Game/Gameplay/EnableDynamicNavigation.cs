// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.BepuPhysics.Navigation.Processors;
using Stride.Core;
using Stride.Core.Collections;
using Stride.Engine;

namespace Gameplay
{
    /// <summary>
    /// Builds the navigation mesh again when a scene streams in or out of the scene of the entity.
    /// </summary>
    public class EnableDynamicNavigation : StartupScript
    {
        private Scene scene;

        public override void Start()
        {
            scene = Entity.Scene;
            scene.Children.CollectionChanged += ScenesOnCollectionChanged;
        }

        public override void Cancel()
        {
            scene.Children.CollectionChanged -= ScenesOnCollectionChanged;
        }

        private void ScenesOnCollectionChanged(object sender, TrackingCollectionChangedEventArgs trackingCollectionChangedEventArgs)
        {
            Services.GetSafeServiceAs<RecastMeshSystem>().RebuildNavMesh();
        }
    }
}
