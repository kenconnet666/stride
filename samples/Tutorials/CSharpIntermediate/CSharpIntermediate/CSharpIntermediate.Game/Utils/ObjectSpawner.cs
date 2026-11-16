// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace CSharpIntermediate.Code
{
    public class ObjectSpawner : SyncScript
    {
        public Prefab ObjectToSpawn;

        private Entity prefabClone = null;

        public override void Start() { }

        public override void Update()
        {

            DebugText.Print("Press S to spawn ball", new Int2(300, 180));

            if (Input.IsKeyPressed(Keys.S))
            {
                prefabClone ??= ObjectToSpawn.Instantiate()[0];
                if (!Entity.Scene.Entities.Contains(prefabClone))
                {
                    Entity.Scene.Entities.Add(prefabClone);
                }

                Entity.Transform.GetWorldTransformation(out Vector3 worldPos, out Quaternion rot, out Vector3 scale);

                // The physics body moves the ball: we teleport the body, and stop it
                var body = prefabClone.Get<BodyComponent>();
                body.Teleport(worldPos, Quaternion.Identity);
                body.LinearVelocity = new Vector3(0);
                body.AngularVelocity = new Vector3(0);
                body.Awake = true;
            }
        }

        public override void Cancel()
        {
            if (prefabClone != null)
            {
                Entity.Scene.Entities.Remove(prefabClone);
                prefabClone = null;
            }
        }
    }
}
