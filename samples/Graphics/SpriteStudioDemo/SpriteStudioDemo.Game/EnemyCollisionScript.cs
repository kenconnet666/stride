// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Contacts;
using Stride.Engine;

namespace SpriteStudioDemo
{
    /// <summary>
    /// Makes the enemy explode when a bullet touches it.
    /// </summary>
    public class EnemyCollisionScript : StartupScript, IContactHandler
    {
        private EnemyScript enemyScript;

        public bool NoContactResponse => false;

        public override void Start()
        {
            enemyScript = Entity.Get<EnemyScript>();
            Entity.Get<BodyComponent>().ContactEventHandler = this;
        }

        void IContactHandler.OnStartedTouching<TManifold>(Contacts<TManifold> contacts)
        {
            if (contacts.Other.Entity.Name != "bullet" || enemyScript.IsGhost) //a ghost ignores the bullets
                return;

            contacts.Other.Entity.Get<BeamScript>().Die();
            enemyScript.Explode();
        }
    }
}
