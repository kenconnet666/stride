// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Threading.Tasks;
using Stride.Engine;
using Stride.BepuPhysics;

namespace GravitySensor
{
    /// <summary>
    /// This script will make sure that all the physics elements of this entity will never be set to sleep status
    /// The physics engine will sometimes set colliders to sleep state to reduce processor usage when there is no motion happening
    /// Those colliders will wake up if an external (an other collider hitting us) collision happens, but in this case we need to prevent this behavior totally,
    /// as there will be no external collision once the motion is 0.
    /// </summary>
    public class NoSleepScript : AsyncScript
    {
        public override Task Execute()
        {
            // A negative threshold keeps a Bepu body awake
            foreach (var physicsElement in Entity.GetAll<BodyComponent>())
            {
                physicsElement.SleepThreshold = -1f;
            }

            return Task.FromResult(0);
        }
    }
}
