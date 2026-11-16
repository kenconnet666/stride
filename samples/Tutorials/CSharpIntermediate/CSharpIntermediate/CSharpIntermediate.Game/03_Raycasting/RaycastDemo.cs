// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using CSharpIntermediate.Code.Extensions;
using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace CSharpIntermediate.Code
{
    public class RaycastDemo : SyncScript
    {
        // The collision layers the raycast can hit. In this scene the triggers are on Layer4: leave it out to ignore them.
        public CollisionMask CollideWith = CollisionMask.Everything;
        public Entity HitPoint;

        private const float maxDistance = 4.0f;
        private Entity laser;
        private BepuSimulation simulation;

        public override void Start()
        {
            //Store the physics simulation object
            simulation = Entity.GetSimulation();
            laser = Entity.FindChild("Laser");
        }

        public override void Update()
        {
            int drawX = 340;
            int drawY = 80;
            DebugText.Print("Press Q and E to raise/lower weapons", new Int2(drawX, drawY));

            var raycastStart = Entity.Transform.Position;
            var raycastDirection = new Vector3(0, 0, 1);
            var raycastEnd = raycastStart + raycastDirection * maxDistance;

            drawY += 40;

            // Send a raycast from the start position in the direction, up to the maximum distance
            if (simulation.RayCast(raycastStart, raycastDirection, maxDistance, out HitInfo hitResult, CollideWith))
            {
                // If we hit something, scale the laser to the distance of the hit point
                HitPoint.Transform.Position = hitResult.Point;
                laser.Transform.Scale.Z = hitResult.Distance;

                DebugText.Print("Hit a collider", new Int2(drawX, drawY));
                DebugText.Print($"Raycast hit distance: {hitResult.Distance}", new Int2(drawX, drawY + 20));
                DebugText.Print($"Raycast hit point: {hitResult.Point.Print()}", new Int2(drawX, drawY + 40));
                DebugText.Print($"Raycast hit entity: {hitResult.Collidable.Entity.Name}", new Int2(drawX, drawY + 60));
            }
            else
            {
                // If we didn't hit anything, scale the laser to match the distance between start and end
                HitPoint.Transform.Position = raycastEnd;
                laser.Transform.Scale.Z = maxDistance;
                DebugText.Print("No collider hit", new Int2(drawX, drawY));
            }
        }
    }
}
