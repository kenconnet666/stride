using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Stride.Core.Threading;
using Stride.Graphics;
using Stride.Graphics.WebGPU;
using Api = Stride.Graphics.WebGPU.WebGPU;

// The engine on a worker: a managed thread with its own JavaScript interop gets the canvas of the page, draws on it
// through the C bindings of the engine's WebGPU backend, and runs code on Stride's thread pool each frame.
[SupportedOSPlatform("browser")]
public static partial class Program
{
    private const int Frames = 120;

    private static WebGpuDeviceContext? context;
    private static WGPUSurface surface;
    private static string? frameError;

    public static void Main()
    {
    }

    /// Resolves to the size of the canvas ("640x360") once the page has given it to this worker, as the target "#canvas".
    [JSImport("receiveCanvas", "engine-worker")]
    private static partial Task<string> ReceiveCanvas();

    /// Calls tick once per animation frame of the worker, then resolves.
    [JSImport("runFrames", "engine-worker")]
    private static partial Task RunFrames([JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Func<int, int> tick, int frames);

    [JSImport("report", "engine-worker")]
    private static partial void Report(string json);

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
        try
        {
            var size = (await ReceiveCanvas()).Split('x');
            context = await WebGpuDeviceContext.CreatePortableAsync();
            var roundtrip = await BindingsRoundtrip();
            ConfigureSurface(uint.Parse(size[0]), uint.Parse(size[1]));
            await RunFrames(Tick, Frames);
            if (frameError != null) throw new InvalidOperationException(frameError);
            await context.WaitForIdleAsync();
            Report($"{{\"bindingsRoundtrip\":{(roundtrip ? "true" : "false")},\"frames\":{Frames},\"lastRed\":{Red(Frames - 1)}}}");
        }
        catch (Exception e)
        {
            Report($"{{\"error\":\"{e.GetType().Name}: {e.Message.Replace('"', '\'').Replace('\\', '/').ReplaceLineEndings(" ")}\"}}");
            throw;
        }
    }

    // A device of the engine's context on this worker: a buffer written through the queue and read back
    private static async Task<bool> BindingsRoundtrip()
    {
        var descriptor = WGPUBufferDescriptor.CreateDefault();
        descriptor.size = 16;
        descriptor.usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst;
        var buffer = CreateBuffer(descriptor);
        try
        {
            WriteBytes(buffer);
            var bytes = await context!.ReadBufferAsync(buffer, 16);
            for (var index = 0; index < bytes.Length; index++)
                if (bytes[index] != index + 1) return false;
            return bytes.Length == 16;
        }
        finally { Api.wgpuBufferRelease(buffer); }
    }

    private static unsafe WGPUBuffer CreateBuffer(WGPUBufferDescriptor descriptor) => Api.wgpuDeviceCreateBuffer(context!.Device, &descriptor);

    private static unsafe void WriteBytes(WGPUBuffer buffer)
    {
        var bytes = stackalloc byte[16];
        for (var i = 0; i < 16; i++) bytes[i] = (byte)(i + 1);
        Api.wgpuQueueWriteBuffer(context!.Queue, buffer, 0, bytes, 16);
        Api.wgpuQueueSubmit(context.Queue, 0, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CanvasSource { public WGPUChainedStruct chain; public WGPUStringView selector; }

    // The surface of the canvas this worker owns: the worker registered it as "#canvas"
    private static unsafe void ConfigureSurface(uint width, uint height)
    {
        var selector = Encoding.UTF8.GetBytes("#canvas\0");
        fixed (byte* text = selector)
        {
            var canvas = new CanvasSource
            {
                chain = new WGPUChainedStruct { sType = (WGPUSType)262144 },
                selector = new WGPUStringView { data = text, length = (nuint)(selector.Length - 1) },
            };
            var descriptor = WGPUSurfaceDescriptor.CreateDefault();
            descriptor.nextInChain = &canvas.chain;
            surface = Api.wgpuInstanceCreateSurface(context!.Instance, &descriptor);
        }
        if (surface.Handle == 0) throw new InvalidOperationException("No surface for the canvas of the worker.");
        var configuration = WGPUSurfaceConfiguration.CreateDefault();
        configuration.device = context.Device;
        configuration.format = WGPUTextureFormat.BGRA8Unorm;
        configuration.usage = WGPUTextureUsage.RenderAttachment;
        configuration.width = width;
        configuration.height = height;
        configuration.presentMode = WGPUPresentMode.Fifo;
        Api.wgpuSurfaceConfigure(surface, &configuration);
        context.ThrowIfFailed();
    }

    private static readonly double[] Scratch = new double[256];

    private static int Red(int frame) => 64 + frame % 128;

    // A frame: a parallel loop on Stride's thread pool, awaited by this thread, then a clear of the canvas
    private static int Tick(int frame)
    {
        try
        {
            Dispatcher.For(0, Scratch.Length, frame, static (frame, i) =>
            {
                var value = (double)(i + frame);
                for (var step = 0; step < 2000; step++) value = Math.Sqrt(value * value + step) * 0.999;
                Scratch[i] = value;
            });
            Clear(new WGPUColor { r = Red(frame) / 255.0, g = 128 / 255.0, b = 64 / 255.0, a = 1 });
        }
        catch (Exception e) { frameError ??= $"frame {frame}: {e.GetType().Name}: {e.Message}"; }
        return Red(frame);
    }

    private static unsafe void Clear(WGPUColor color)
    {
        var acquired = default(WGPUSurfaceTexture);
        Api.wgpuSurfaceGetCurrentTexture(surface, &acquired);
        if (acquired.texture.Handle == 0) throw new InvalidOperationException($"No surface image: {acquired.status}.");
        WGPUCommandEncoder encoder = default;
        WGPUCommandBuffer buffer = default;
        WGPUTextureView view = default;
        try
        {
            var encoderDescriptor = WGPUCommandEncoderDescriptor.CreateDefault();
            encoder = Api.wgpuDeviceCreateCommandEncoder(context!.Device, &encoderDescriptor);
            var viewDescriptor = WGPUTextureViewDescriptor.CreateDefault();
            view = Api.wgpuTextureCreateView(acquired.texture, &viewDescriptor);
            var attachment = WGPURenderPassColorAttachment.CreateDefault();
            attachment.view = view;
            attachment.loadOp = WGPULoadOp.Clear;
            attachment.storeOp = WGPUStoreOp.Store;
            attachment.clearValue = color;
            var pass = WGPURenderPassDescriptor.CreateDefault();
            pass.colorAttachmentCount = 1;
            pass.colorAttachments = &attachment;
            var passEncoder = Api.wgpuCommandEncoderBeginRenderPass(encoder, &pass);
            Api.wgpuRenderPassEncoderEnd(passEncoder);
            Api.wgpuRenderPassEncoderRelease(passEncoder);
            var bufferDescriptor = WGPUCommandBufferDescriptor.CreateDefault();
            buffer = Api.wgpuCommandEncoderFinish(encoder, &bufferDescriptor);
            Api.wgpuQueueSubmit(context.Queue, 1, &buffer);
            context.ThrowIfFailed();
        }
        finally
        {
            if (buffer.Handle != 0) Api.wgpuCommandBufferRelease(buffer);
            if (encoder.Handle != 0) Api.wgpuCommandEncoderRelease(encoder);
            if (view.Handle != 0) Api.wgpuTextureViewRelease(view);
            Api.wgpuTextureRelease(acquired.texture);
        }
    }
}
