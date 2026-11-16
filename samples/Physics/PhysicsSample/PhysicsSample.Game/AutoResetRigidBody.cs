// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Threading.Tasks;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Events;
using Stride.BepuPhysics;

namespace PhysicsSample
{
    public class AutoResetRigidbody : AsyncScript
    {
        private Vector3 startLocation;
        private Quaternion startRotation;

        public Entity TriggerEntity;

        public override async Task Execute()
        {
            startLocation = Entity.Transform.Position;
            startRotation = Entity.Transform.Rotation;

            //grab a reference to the falling sphere's rigidbody
            var rigidBody = Entity.Get<BodyComponent>();

            var triggerKey = TriggerEntity.Get<Trigger>().TriggerEvent;
            var receiver = new EventReceiver<bool>(triggerKey);

            while (!CancellationToken.IsCancellationRequested)
            {
                var state = await receiver.ReceiveAsync();
				
				if (CancellationToken.IsCancellationRequested)
                    return;
				
                if (state)
                {
                    //switch to dynamic and awake the rigid body
                    rigidBody.Kinematic = false;
                    rigidBody.Awake = true; //need to awake to object
                }
                else
                {
                    //when out revert to kinematic and old starting position
                    rigidBody.Kinematic = true;
                    //reset position
                    rigidBody.Teleport(startLocation, startRotation);
                    rigidBody.LinearVelocity = Vector3.Zero;
                    rigidBody.AngularVelocity = Vector3.Zero;
                }
            }
        }
    }
}
