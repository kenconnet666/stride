// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.BepuPhysics;
using Stride.Core;
using Stride.Engine;
using Stride.Engine.Events;
using BepuTrigger = Stride.BepuPhysics.Definitions.Trigger;

namespace PhysicsSample
{
    /// <summary>
    /// Broadcasts true when a collidable enters the Bepu trigger of the entity, and false when it leaves.
    /// </summary>
    public class Trigger : StartupScript
    {
        [DataMemberIgnore]
        public EventKey<bool> TriggerEvent = new EventKey<bool>();

        private BepuTrigger trigger;

        public override void Start()
        {
            // The collision matrix of the simulation chooses which collidables touch the trigger
            trigger = (BepuTrigger)Entity.Get<CollidableComponent>().ContactEventHandler;
            trigger.OnEnter += OnEnter;
            trigger.OnLeave += OnLeave;
        }

        public override void Cancel()
        {
            trigger.OnEnter -= OnEnter;
            trigger.OnLeave -= OnLeave;
        }

        private void OnEnter(CollidableComponent @this, CollidableComponent other) => TriggerEvent.Broadcast(true);

        private void OnLeave(CollidableComponent @this, CollidableComponent other) => TriggerEvent.Broadcast(false);
    }
}
