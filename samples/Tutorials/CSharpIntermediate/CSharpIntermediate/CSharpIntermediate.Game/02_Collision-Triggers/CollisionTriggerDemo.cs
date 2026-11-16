// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Generic;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace CSharpIntermediate.Code
{
    public class CollisionTriggerDemo : SyncScript
    {
        StaticComponent staticCollider;
        Trigger trigger;
        string collisionStatus = "";

        // The collidables that are inside the trigger right now
        readonly HashSet<CollidableComponent> inside = new();

        public override void Start()
        {
            // Retrieve the static collider of the current entity
            staticCollider = Entity.Get<StaticComponent>();

            // Its contact event handler is a Trigger: other collidables go through it, and it tells when they enter and leave
            trigger = (Trigger)staticCollider.ContactEventHandler;

            // When a collidable enters or leaves the trigger, execute the Entered or Left method
            trigger.OnEnter += Entered;
            trigger.OnLeave += Left;
        }

        private void Entered(CollidableComponent triggerCollider, CollidableComponent ballCollider)
        {
            // An object has 'entered' our trigger
            inside.Add(ballCollider);
            collisionStatus = ballCollider.Entity.Name + " entered " + triggerCollider.Entity.Name;
        }

        private void Left(CollidableComponent triggerCollider, CollidableComponent ballCollider)
        {
            // An object has 'left' our trigger
            inside.Remove(ballCollider);
            collisionStatus = ballCollider.Entity.Name + " left " + triggerCollider.Entity.Name;
        }

        public override void Update()
        {
            // The trigger can have 0, 1, or multiple collidables inside it in a single frame
            int drawX = 500, drawY = 300;
            foreach (var collidable in inside)
            {
                DebugText.Print("Inside the trigger: " + collidable.Entity.Name, new Int2(drawX, drawY += 20));
            }

            DebugText.Print(collisionStatus, new Int2(500, 400));
        }

        public override void Cancel()
        {
            trigger.OnEnter -= Entered;
            trigger.OnLeave -= Left;
        }
    }
}
