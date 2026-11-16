// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Threading.Tasks;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.Engine;
using Stride.Rendering;

namespace CSharpIntermediate.Code
{
    public class AsyncCollisionTriggerDemo : AsyncScript
    {
        private Material yellowMaterial;

        public override async Task Execute()
        {
            // Store the trigger of the static collider
            var trigger = (Trigger)Entity.Get<StaticComponent>().ContactEventHandler;

            //Preload some materials
            yellowMaterial = Content.Load<Material>("Materials/Yellow");

            while (Game.IsRunning)
            {
                // Wait for an entity to enter the trigger
                var ballCollider = await Entered(trigger);

                // Store current material
                var modelComponent = ballCollider.Entity.Get<ModelComponent>();
                var originalMaterial = modelComponent.Materials[0];

                // Change the material on the entity
                modelComponent.Materials[0] = yellowMaterial;

                // Wait for the entity to exit the trigger
                await Left(trigger, ballCollider);

                // Change the material back to the original one
                modelComponent.Materials[0] = originalMaterial;
            }
        }

        // A task that completes when a collidable enters the trigger: the collidable is its result
        private static Task<CollidableComponent> Entered(Trigger trigger)
        {
            var entered = new TaskCompletionSource<CollidableComponent>();
            TriggerDelegate onEnter = null;
            onEnter = (_, other) =>
            {
                trigger.OnEnter -= onEnter;
                entered.SetResult(other);
            };
            trigger.OnEnter += onEnter;
            return entered.Task;
        }

        // A task that completes when the collidable leaves the trigger
        private static Task Left(Trigger trigger, CollidableComponent collidable)
        {
            var left = new TaskCompletionSource();
            TriggerDelegate onLeave = null;
            onLeave = (_, other) =>
            {
                if (other != collidable)
                    return;
                trigger.OnLeave -= onLeave;
                left.SetResult();
            };
            trigger.OnLeave += onLeave;
            return left.Task;
        }

        public override void Cancel()
        {
            Content.Unload(yellowMaterial);
        }
    }
}
