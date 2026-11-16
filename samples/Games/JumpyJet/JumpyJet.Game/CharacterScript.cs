// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Events;
using Stride.Input;
using BepuPhysics.Collidables;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.Rendering.Sprites;

namespace JumpyJet
{
    /// <summary>
    /// CharacterScript is controlled by a user.
    /// The control is as follow, tapping a screen/clicking a mouse will make the agent jump up.
    /// </summary>
    public class CharacterScript : AsyncScript
    {
        private EventReceiver gameResetListener = new EventReceiver(GameGlobals.GameResetEventKey);
        private EventReceiver gameStartedListener = new EventReceiver(GameGlobals.GameStartedEventKey);

        private static readonly Vector3 Gravity = new Vector3(0, -17, 0);
        private static readonly Vector3 StartPos = new Vector3(-1, 0, 0);
        private static readonly Vector3 StartVelocity = new Vector3(0, 7, 0);

        private const float TopLimit = (568 - 200) * GameGlobals.GamePixelToUnitScale;
        private const float NormalVelocityY = 6.5f;
        private const float VelocityAboveTopLimit = 2f;
        private const int FlyingSpriteFrameIndex = 1;
        private const int FallingSpriteFrameIndex = 0;

        private Vector3 position;
        private Vector3 rotation;

        private bool isRunning;
        private Vector3 velocity;

        // The pipes and the triggers between them are kinematic, like the character: Bepu makes no contacts between
        // them, so the character looks for what its colliders overlap
        private readonly List<OverlapInfo> overlaps = new List<OverlapInfo>();
        private readonly HashSet<CollidableComponent> passedTriggers = new HashSet<CollidableComponent>();

        public void Start()
        {
            position = StartPos;
            velocity = StartVelocity;

            Reset();

            Script.AddTask(CountPassedPipes);
            Script.AddTask(DetectGameOver);
        }

        /// <summary>
        /// Reset CharacterScript parameters: position, velocity and set state.
        /// </summary>
        public void Reset()
        {
            position.Y = 0;
            rotation.Z = 0f;
            UpdateTransformation();

            velocity = StartVelocity;
            isRunning = false;

            var provider = Entity.Get<SpriteComponent>().SpriteProvider as SpriteFromSheet;
            if (provider != null)
                provider.CurrentFrame = FallingSpriteFrameIndex;
        }

        /// <summary>
        /// Update the agent according to its states: {Idle, Alive, Die}
        /// </summary>
        public async Task CountPassedPipes()
        {
            while (Game.IsRunning)
            {
                await Script.NextFrame();

                // The trigger between two pipes is on the layer of the Bullet group CustomFilter1
                var touching = Overlapping(CollisionMask.Layer6).ToList();
                foreach (var trigger in touching)
                {
                    if (passedTriggers.Add(trigger))
                        GameGlobals.PipePassedEventKey.Broadcast();
                }
                passedTriggers.IntersectWith(touching);
            }
        }

        /// <summary>
        /// Update the agent according to its states: {Idle, Alive, Die}
        /// </summary>
        public async Task DetectGameOver()
        {
            while (Game.IsRunning)
            {
                await Script.NextFrame();

                // detect collisions with the pipes and the ground, on the default layer
                if (isRunning && Overlapping(CollisionMask.Layer0).Any())
                {
                    isRunning = false;
                    GameGlobals.GameOverEventKey.Broadcast();
                }
            }
        }

        /// <summary>
        /// Update the agent according to its states: {Idle, Alive, Die}
        /// </summary>
        public override async Task Execute()
        {
            Start();

            while (Game.IsRunning)
            {
                await Script.NextFrame();

                if (gameResetListener.TryReceive())
                    Reset();

                if (gameStartedListener.TryReceive())
                    isRunning = true;

                if (!isRunning)
                    continue;

                var elapsedTime = (float)Game.UpdateTime.WarpElapsed.TotalSeconds;

                // apply impulse on the touch/space
                if (Input.IsKeyPressed(Keys.Space) || UserTappedScreen())
                    velocity.Y = position.Y > TopLimit ? VelocityAboveTopLimit : NormalVelocityY;

                // update position/velocity
                velocity += Gravity * elapsedTime;
                position += velocity * elapsedTime;

                // update animation and rotation value
                UpdateAgentAnimation();

                // update the position/rotation
                UpdateTransformation();
            }
        }

        // The collidables the colliders of the character overlap, but itself
        private IEnumerable<CollidableComponent> Overlapping(CollisionMask mask)
        {
            var body = Entity.Get<BodyComponent>();
            if (body?.Collider is not CompoundCollider compound)
                yield break;
            Entity.Transform.WorldMatrix.Decompose(out _, out Quaternion rotation, out var position);
            foreach (var collider in compound.Colliders)
            {
                var pose = new RigidPose { Position = position + Vector3.Transform(collider.PositionLocal, rotation), Orientation = collider.RotationLocal * rotation };
                overlaps.Clear();
                switch (collider)
                {
                    case CapsuleCollider capsule: Entity.GetSimulation().Overlap(new Capsule(capsule.Radius, capsule.Length), pose, overlaps, mask); break;
                    case BoxCollider box: Entity.GetSimulation().Overlap(new Box(box.Size.X, box.Size.Y, box.Size.Z), pose, overlaps, mask); break;
                    case SphereCollider sphere: Entity.GetSimulation().Overlap(new Sphere(sphere.Radius), pose, overlaps, mask); break;
                }
                foreach (var overlap in overlaps)
                {
                    if (overlap.Collidable != body)
                        yield return overlap.Collidable;
                }
            }
        }

        private void UpdateTransformation()
        {
            Entity.Transform.Position = position;
            Entity.Transform.RotationEulerXYZ = rotation;
        }

        private bool UserTappedScreen()
        {
            return Input.PointerEvents.Any(pointerEvent => pointerEvent.EventType == PointerEventType.Pressed);
        }

        private void UpdateAgentAnimation()
        {
            var isFalling = velocity.Y < 0;
            var rotationSign = isFalling ? -1 : 1;

            // Set falling sprite frame
            var provider = Entity.Get<SpriteComponent>().SpriteProvider as SpriteFromSheet;
            if (provider != null)
                provider.CurrentFrame = isFalling ? FallingSpriteFrameIndex : FlyingSpriteFrameIndex;

            // Rotate a sprite
            rotation.Z += rotationSign * MathUtil.Pi * 0.01f;
            if (rotationSign * rotation.Z > Math.PI / 10f)
                rotation.Z = rotationSign * MathUtil.Pi / 10f;
        }
    }
}
