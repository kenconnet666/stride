// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Events;
using BepuPhysics.Collidables;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using ThirdPersonPlatformer.Player;

namespace ThirdPersonPlatformer.Camera
{
    public class ThirdPersonCamera : SyncScript
    {
        /// <summary>
        /// Starting camera distance from the target
        /// </summary>
        public float DefaultDistance { get; set; } = 6f;

        /// <summary>
        /// Minimum camera distance from the target
        /// </summary>
        public float MinimumDistance { get; set; } = 0.4f;

        /// <summary>
        /// Radius of the sphere swept from the target to the camera to hold the camera; 0 casts a ray
        /// </summary>
        public float ConeRadius { get; set; } = 1.25f;

        /// <summary>
        /// Check to invert the horizontal camera movement
        /// </summary>
        public bool InvertX { get; set; } = false;

        /// <summary>
        /// Minimum camera distance from the target
        /// </summary>
        public float MinVerticalAngle { get; set; } = -20f;

        /// <summary>
        /// Maximum camera distance from the target
        /// </summary>
        public float MaxVerticalAngle { get; set; } = 70f;

        /// <summary>
        /// Check to invert the vertical camera movement
        /// </summary>
        public bool InvertY { get; set; } = false;

        /// <summary>
        /// Maximum rotation speed for the camera around the target in degrees per second
        /// </summary>
        public float RotationSpeed { get; set; } = 360f;

        /// <summary>
        /// Maximum rotation speed for the camera around the target in degrees per second
        /// </summary>
        public float VerticalSpeed { get; set; } = 65f;

        private Vector3 cameraRotationXYZ = new Vector3(-20, 45, 0);
        private Vector3 targetRotationXYZ = new Vector3(-20, 45, 0);
        private readonly EventReceiver<Vector2> cameraDirectionEvent = new EventReceiver<Vector2>(PlayerInput.CameraDirectionEventKey);

        /// <summary>
        /// Casts between the camera and its target. The script assumes the camera is a child entity of its target.
        /// </summary>
        private void UpdateCameraRaycast()
        {
            var maxLength = DefaultDistance;
            var cameraDirection = new Vector3(0, 0, 1);
            Entity.GetParent().Transform.Rotation.Rotate(ref cameraDirection);
            var start = Entity.GetParent().Transform.WorldMatrix.TranslationVector;
            // Only the objects of the default layer hold the camera: the player (another layer) and the poles do not
            const CollisionMask blocking = CollisionMask.Layer0;
            var simulation = Entity.GetSimulation();

            var hit = ConeRadius <= 0
                ? simulation.RayCast(start, cameraDirection, DefaultDistance, out var result, blocking)
                : simulation.SweepCast(new Sphere(ConeRadius), new RigidPose { Position = start }, new BodyVelocity { Linear = cameraDirection },
                    DefaultDistance, out result, blocking);
            if (hit)
                maxLength = Math.Min(DefaultDistance, result.Distance);

            if (maxLength < MinimumDistance)
                maxLength = MinimumDistance;

            Entity.Transform.Position.Z = maxLength;
        }

        /// <summary>
        /// Raycast between the camera and its target. The script assumes the camera is a child entity of its target.
        /// </summary>
        private void UpdateCameraOrientation()
        {
            // Camera movement from player input
            Vector2 cameraMovement;
            cameraDirectionEvent.TryReceive(out cameraMovement);

            if (InvertY) cameraMovement.Y *= -1;
            targetRotationXYZ.X += cameraMovement.Y * VerticalSpeed;
            targetRotationXYZ.X = Math.Max(targetRotationXYZ.X, -MaxVerticalAngle);
            targetRotationXYZ.X = Math.Min(targetRotationXYZ.X, -MinVerticalAngle);

            if (InvertX) cameraMovement.X *= -1;
            targetRotationXYZ.Y -= cameraMovement.X * RotationSpeed;

            // Very simple lerp to allow smoother transition of the camera towards its desired destination. You can change this behavior with a different one, better suited for your game.
            cameraRotationXYZ = Vector3.Lerp(cameraRotationXYZ, targetRotationXYZ, 0.25f);
            Entity.GetParent().Transform.RotationEulerXYZ = new Vector3(MathUtil.DegreesToRadians(cameraRotationXYZ.X), MathUtil.DegreesToRadians(cameraRotationXYZ.Y), 0);
        }

        public override void Update()
        {
            UpdateCameraRaycast();

            UpdateCameraOrientation();
        }

        public override void Start()
        {
            base.Start();


            if (Entity.GetParent() == null) throw new ArgumentException("ThirdPersonCamera should be placed as a child entity of its target entity!");
        }
    }
}
