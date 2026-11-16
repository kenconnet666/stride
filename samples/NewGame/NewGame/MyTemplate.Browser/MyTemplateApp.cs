using Stride.Engine;

// The game starts and this program ends: the browser then asks for each frame.
await BrowserGame.RunAsync(() => new Game(), new BrowserGameOptions { GameAssemblies = ["MyTemplate.Game"] });
