using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Stride.Core.Threading;

// Checks what the multithreaded browser runtime gives Stride: threads, blocking waits, and a thread pool whose
// parallel loops run on several of them.
[SupportedOSPlatform("browser")]
public static partial class Program
{
    public static void Main()
    {
    }

    [JSImport("report", "probe")]
    private static partial void Report(string json);

    /// Runs the checks on a thread of the pool and gives the page their results through its report function.
    [JSExport]
    public static Task Run() => Task.Run(() =>
    {
        var report = new Dictionary<string, object?>
        {
            ["multithreadingSupported"] = RuntimeFeature.IsMultithreadingSupported,
            ["processorCount"] = Environment.ProcessorCount,
        };
        report["strideWorkerThreads"] = Stride.Core.Threading.ThreadPool.Instance.WorkerThreadsCount;

        // A thread of its own, joined with a blocking wait
        var threadId = 0;
        var thread = new Thread(() => threadId = Environment.CurrentManagedThreadId);
        thread.Start();
        thread.Join();
        report["threadRanElsewhere"] = threadId != 0 && threadId != Environment.CurrentManagedThreadId;

        // A lock that another thread holds for a while
        var gate = new object();
        using var held = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (gate) { held.Set(); Thread.Sleep(50); } });
        holder.Start();
        held.Wait();
        var waited = Stopwatch.StartNew();
        lock (gate) { }
        report["blockedOnLockMs"] = Math.Round(waited.Elapsed.TotalMilliseconds, 1);
        holder.Join();

        // Stride's parallel loop: which threads take part, and how much faster than one
        var threads = new ConcurrentDictionary<int, int>();
        var results = new double[64];
        var sequential = Stopwatch.StartNew();
        for (var i = 0; i < results.Length; i++) results[i] = Work(i);
        sequential.Stop();
        var expected = results.Sum();
        Array.Clear(results);
        var parallel = Stopwatch.StartNew();
        Dispatcher.For(0, results.Length, (Threads: threads, Results: results), static (state, i) =>
        {
            state.Threads.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, count) => count + 1);
            state.Results[i] = Work(i);
        });
        parallel.Stop();
        report["dispatcherThreads"] = threads.Count;
        report["dispatcherResultMatches"] = Math.Abs(results.Sum() - expected) < 1e-6;
        report["sequentialMs"] = Math.Round(sequential.Elapsed.TotalMilliseconds, 1);
        report["parallelMs"] = Math.Round(parallel.Elapsed.TotalMilliseconds, 1);
        report["speedup"] = Math.Round(sequential.Elapsed.TotalMilliseconds / Math.Max(0.001, parallel.Elapsed.TotalMilliseconds), 2);
        // The values are booleans and numbers: written by hand, the serializer needs reflection that trimming removes
        Report("{" + string.Join(",", report.Select(x => $"\"{x.Key}\":{FormattableString.Invariant($"{x.Value}").ToLowerInvariant()}")) + "}");
    });

    private static double Work(int seed)
    {
        var value = (double)seed;
        for (var i = 0; i < 400_000; i++) value = Math.Sqrt(value * value + i) * 0.999;
        return value;
    }
}
