using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Stride.Core.Threading;
using Stride.Graphics;
using Stride.Graphics.WebGPU;
using Api = Stride.Graphics.WebGPU.WebGPU;

// The engine on a worker: a managed thread with its own JavaScript interop draws with WebGPU on the canvas of the page
// and runs managed code that uses Stride's thread pool each frame.
[SupportedOSPlatform("browser")]
public static partial class Program
{
    private const int Frames = 120;

    public static void Main()
    {
    }

    [JSImport("run", "engine-worker")]
    private static partial Task<string> RunWorker([JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Func<int, int> tick, int frames, bool bindingsRoundtrip);

    /// Starts the worker of the engine and completes when it has drawn its frames.
    [JSExport]
    public static Task Run()
    {
        // The API for a managed thread with JavaScript interop is in the runtime, not yet in its reference assembly
        var worker = Type.GetType("System.Runtime.InteropServices.JavaScript.JSWebWorker, System.Runtime.InteropServices.JavaScript", throwOnError: true)!;
        var run = worker.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static, [typeof(Func<Task>)])
            ?? throw new MissingMethodException("JSWebWorker.RunAsync(Func<Task>)");
        return (Task)run.Invoke(null, [(Func<Task>)EngineThread])!;
    }

    private static async Task EngineThread()
    {
        await JSHost.ImportAsync("engine-worker", "../engine-worker.js");
        await RunWorker(Tick, Frames, await BindingsRoundtrip());
    }

    // The C bindings of the engine on this worker: a device, a buffer written through the queue and read back
    private static async Task<bool> BindingsRoundtrip()
    {
        using var context = await WebGpuDeviceContext.CreatePortableAsync();
        var buffer = CreateBuffer(context);
        try
        {
            WriteBytes(context, buffer);
            var bytes = await context.ReadBufferAsync(buffer, 16);
            for (var index = 0; index < bytes.Length; index++)
                if (bytes[index] != index + 1) return false;
            await context.WaitForIdleAsync();
            return bytes.Length == 16;
        }
        finally { Api.wgpuBufferRelease(buffer); }
    }

    private static unsafe WGPUBuffer CreateBuffer(WebGpuDeviceContext context)
    {
        var descriptor = WGPUBufferDescriptor.CreateDefault();
        descriptor.size = 16;
        descriptor.usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst;
        return Api.wgpuDeviceCreateBuffer(context.Device, &descriptor);
    }

    private static unsafe void WriteBytes(WebGpuDeviceContext context, WGPUBuffer buffer)
    {
        var bytes = stackalloc byte[16];
        for (var i = 0; i < 16; i++) bytes[i] = (byte)(i + 1);
        Api.wgpuQueueWriteBuffer(context.Queue, buffer, 0, bytes, 16);
        Api.wgpuQueueSubmit(context.Queue, 0, null);
    }

    private static readonly double[] Scratch = new double[256];

    // What a frame of the engine does on its thread pool, in small: a parallel loop, awaited by the calling thread
    private static int Tick(int frame)
    {
        Dispatcher.For(0, Scratch.Length, frame, static (frame, i) =>
        {
            var value = (double)(i + frame);
            for (var step = 0; step < 2000; step++) value = Math.Sqrt(value * value + step) * 0.999;
            Scratch[i] = value;
        });
        return 64 + frame % 128;
    }
}
