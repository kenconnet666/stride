import { dotnet } from './_framework/dotnet.js';

// The program starts the game and returns. The runtime stays: the game draws a frame for each animation frame.
// globalThis.strideGame has the state of the game (status, frames, error) for a page that wants to show it.
const runtime = await dotnet.create();
await runtime.runMain();
