using System;
using Stride.Core;
using System.Threading.Tasks;
using Stride.Engine;
using Stride.Audio;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Platformer2D.Player;

namespace Platformer2D.Gameplay;

public class CollectCoin : AsyncScript
{
    // A trigger: its contact event handler is a Trigger
    public required StaticComponent StaticColliderComponent { get; init; }
    
    [Display("Collect Coin")]
    public Sound? SoundEffect { get; set; }
    private SoundInstance? sfxInstance;

    public override async Task Execute()
    {
        // Waits for the Character to enter the coin
        var collected = false;
        var trigger = (Trigger)StaticColliderComponent.ContactEventHandler!;
        trigger.OnEnter += (_, other) => collected |= other.Entity.Get<PlayerController>() != null;
        while (Game.IsRunning)
        {
            await Script.NextFrame();
            if (collected)
            {
                // Disable Sprite
                Entity.Get<SpriteComponent>().Enabled = false;
                sfxInstance = SoundEffect?.CreateInstance();
                 
                // Play Sound
                sfxInstance?.Play();
                 
                // Disable Coin Object
                await Task.Delay(TimeSpan.FromSeconds(3));
                Entity.Scene = null;
            }
        }
    }
}
