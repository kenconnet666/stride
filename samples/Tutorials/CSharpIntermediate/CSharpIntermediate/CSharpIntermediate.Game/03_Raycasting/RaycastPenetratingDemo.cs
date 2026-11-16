// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Generic;
using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace CSharpIntermediate.Code
{
    public class RaycastPenetratingDemo : SyncScript
    {
        // The collision layers the raycast can hit. In this scene the triggers are on Layer4: leave it out to ignore them.
        public CollisionMask CollideWith = CollisionMask.Everything;

        private Entity laser;
        private const float maxDistance = 3.0f;
        private BepuSimulation simulation;
        private readonly List<HitInfo> hitResults = new();

        public override void Start()
        {
            simulation = Entity.GetSimulation();
            laser = Entity.FindChild("Laser");
        }

        public override void Update()
        {
            int drawX = 700;
            int drawY = 80;
            DebugText.Print("Raycast penetration demo", new Int2(drawX, drawY));

            var raycastStart = Entity.Transform.Position;
            var raycastDirection = new Vector3(0, 0, -1);
            laser.Transform.Scale.Z = maxDistance;

            // The raycast adds every hit along its way to the list
            hitResults.Clear();
            simulation.RayCastPenetrating(raycastStart, raycastDirection, maxDistance, hitResults, CollideWith);

            drawY += 40;
            if (hitResults.Count > 0)
            {
                DebugText.Print($"Raycast has hit {hitResults.Count} object(s)", new Int2(drawX, drawY));

                foreach (var hitResult in hitResults)
                {
                    drawY += 20;
                    DebugText.Print($"- Raycast has hit: {hitResult.Collidable.Entity.Name}", new Int2(drawX, drawY));
                }
            }
            else
            {
                DebugText.Print("No collider hit", new Int2(drawX, drawY));
            }
        }
    }
}
