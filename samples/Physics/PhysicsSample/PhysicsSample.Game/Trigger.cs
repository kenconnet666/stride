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
    /// Broadcasts true when a character enters the Bepu trigger of the entity, and false when it leaves.
    /// </summary>
    /// <remarks>
    /// The crates sit in the trigger too: they share its collision layer, so the trigger leaves them out itself.
    /// </remarks>
    public class Trigger : StartupScript
    {
        [DataMemberIgnore]
        public EventKey<bool> TriggerEvent = new EventKey<bool>();

        private BepuTrigger trigger;

        public override void Start()
        {
            trigger = (BepuTrigger)Entity.Get<CollidableComponent>().ContactEventHandler;
            trigger.OnEnter += OnEnter;
            trigger.OnLeave += OnLeave;
        }

        public override void Cancel()
        {
            trigger.OnEnter -= OnEnter;
            trigger.OnLeave -= OnLeave;
        }

        private void OnEnter(CollidableComponent @this, CollidableComponent other)
        {
            if (other is CharacterComponent) TriggerEvent.Broadcast(true);
        }

        private void OnLeave(CollidableComponent @this, CollidableComponent other)
        {
            if (other is CharacterComponent) TriggerEvent.Broadcast(false);
        }
    }
}
