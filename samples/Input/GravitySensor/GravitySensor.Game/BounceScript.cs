// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.BepuPhysics;
using Stride.Engine;

namespace GravitySensor
{
    /// <summary>
    /// This script makes the contacts of each collidable of the entity springy, so that it bounces
    /// </summary>
    public class BounceScript : StartupScript
    {
        public override void Start()
        {
            // Bepu has no restitution: an undamped contact spring bounces
            foreach (var physicsElement in Entity.GetAll<CollidableComponent>())
            {
                physicsElement.SpringDampingRatio = 0f;
            }
        }
    }
}
