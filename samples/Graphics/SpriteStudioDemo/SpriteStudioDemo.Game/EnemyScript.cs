// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.Core.Mathematics;
using Stride.Engine;
using BepuPhysics;
using Stride.BepuPhysics;
using System;
using System.Threading.Tasks;
using Stride.Animations;
using System.Diagnostics;

namespace SpriteStudioDemo
{
    public class EnemyScript : AsyncScript
    {
        private const float enemyInitPositionY = 20;

        // enemy position
        private const float gameWidthX = 16f; // from -8f to 8f
        private const float gameWidthHalfX = gameWidthX / 2f;

        private const float enemyLifeBase = 3.0f;
        private float enemyLife = 3.0f;

        private BodyComponent rigidbodyElement;

        /// <summary>
        /// Whether bullets go through the enemy: while it waits at the top, and while it explodes.
        /// </summary>
        public bool IsGhost { get; private set; }
        private AnimationComponent animationComponent;

        PlayingAnimation playingAnimation;

        // random
        private static readonly int seed = Environment.TickCount;
        private static readonly Random enemyRandomLocal = new Random(seed);

        private async Task Reset()
        {
            rigidbodyElement.Kinematic = true; //stop motion
            IsGhost = true; //bullets will go thru

            var random = enemyRandomLocal;
            // Appearance position
            var position = Entity.Transform.Position;
            position.Y = enemyInitPositionY;
            position.X = (((float)(random.NextDouble())) * gameWidthX) - gameWidthHalfX;
            rigidbodyElement.LinearVelocity = Vector3.Zero;
            rigidbodyElement.Teleport(position, Quaternion.Identity);
            // Waiting time
            enemyLife = enemyLifeBase + (enemyLifeBase * (float)random.NextDouble());


            if (playingAnimation == null || playingAnimation.Name != "Wait")
            {
                playingAnimation = animationComponent.Play("Wait");
            }

            await Script.NextFrame();

            rigidbodyElement.Kinematic = false;
            IsGhost = false;
            LockRotation();
            rigidbodyElement.Awake = true;
        }

        // The enemy only falls: an infinite inertia keeps it from turning. Becoming dynamic again gives back the inertia
        // of the collider.
        private void LockRotation()
        {
            var inertia = rigidbodyElement.BodyInertia;
            inertia.InverseInertiaTensor = default;
            rigidbodyElement.BodyInertia = inertia;
        }

        Task exploding;

        public void Explode()
        {
            rigidbodyElement.Kinematic = true;
            IsGhost = true;

            if (playingAnimation == null || playingAnimation.Name != "Dead")
            {
                playingAnimation = animationComponent.Play("Dead");
            }

            exploding = WaitMs(1500);
        }

        readonly Stopwatch _watch = Stopwatch.StartNew();

        public async Task WaitMs(int ms)
        {
            var start = _watch.ElapsedMilliseconds;
            while (_watch.ElapsedMilliseconds < start + ms)
            {
                await Script.NextFrame();
            }
        }

        public override async Task Execute()
        {
            animationComponent = Entity.Get<AnimationComponent>();

            rigidbodyElement = Entity.Get<BodyComponent>();

            await Reset();

            while (Game.IsRunning)
            {
                await WaitMs((int)(enemyLife * 1000));

                if (exploding != null)
                {
                    await exploding;
                    exploding = null;
                }

                await Reset();
            }
        }
    }
}
