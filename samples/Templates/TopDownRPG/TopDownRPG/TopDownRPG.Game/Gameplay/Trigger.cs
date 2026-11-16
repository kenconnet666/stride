// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.BepuPhysics;
using Stride.Core;
using Stride.Engine;
using Stride.Engine.Events;
using BepuTrigger = Stride.BepuPhysics.Definitions.Trigger;

namespace TopDownRPG.Gameplay
{
    public enum CollisionEventType
    {
        /// <summary>
        /// Will broadcast an event only when the collision starts
        /// </summary>
        [Display("On Start")]
        StartOnly,

        /// <summary>
        /// Will broadcast an event only when the collision ends
        /// </summary>
        [Display("On End")]
        EndOnly,

        /// <summary>
        /// Will broadcast an event both when the collision starts and when it ends
        /// </summary>
        [Display("On Start and End")]
        StartAndEnd,
    }

    /// <summary>
    /// Broadcasts when a collidable enters or leaves the Bepu trigger of the entity.
    /// </summary>
    public class Trigger : StartupScript
    {
        [Display("Condition")]
        public CollisionEventType TriggerCondition { get; set; } = CollisionEventType.StartOnly;

        [DataMemberIgnore]
        public EventKey<bool> TriggerEvent = new EventKey<bool>();

        private BepuTrigger trigger;

        public override void Start()
        {
            // The collision matrix of the simulation chooses which collidables touch the trigger
            trigger = (BepuTrigger)Entity.Get<CollidableComponent>().ContactEventHandler;
            if (TriggerCondition != CollisionEventType.EndOnly)
                trigger.OnEnter += OnEnter;
            if (TriggerCondition != CollisionEventType.StartOnly)
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
