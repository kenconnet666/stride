// Copyright (c) .NET Foundation and Contributors.
// Distributed under the MIT license. See LICENSE.md in the project root.
#if STRIDE_GRAPHICS_API_WEBGPU
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Stride.Graphics.WebGPU;
using Api = Stride.Graphics.WebGPU.WebGPU;

namespace Stride.Graphics;

internal sealed unsafe partial class WebGpuDeviceContext
{
    private sealed class ReadMap
    {
        public nint Token;
        public ulong Length;
        public int Status;
    }
    private static readonly ConcurrentDictionary<nint, ReadMap> pendingReadCallbacks = new();
    private readonly Dictionary<WGPUBuffer, ReadMap> pendingReadBuffers = new();
    // In a browser, Unmap ends a pending map at once for the C API, but the emdawnwebgpu glue also handles the rejection
    // of the JavaScript map, a microtask later, and then forgets the next map of that buffer: its range can no longer be
    // read. So a buffer whose map was aborted is mapped again in a later frame only, once that rejection came.
    private readonly HashSet<WGPUBuffer> readsAbortedThisFrame = new();

    /// <summary>
    /// Readable buffers that a queue write or a mapping at creation gave data since the last submission.
    /// </summary>
    /// <remarks>
    /// wgpu-native applies such data at the next submission only: a map of one of these buffers submits first. A browser
    /// applies it at once, in the order of the queue.
    /// </remarks>
    internal readonly HashSet<nint> ReadableWritesBeforeSubmit = new();

    public bool TryMapBufferRead(WGPUBuffer buffer, ulong length, out nint pointer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (asyncReadBuffers.ContainsKey(buffer)) throw new InvalidOperationException("Await asynchronous readback before reusing this buffer.");
        pointer = 0;
        lock (pendingReadBuffers)
        {
            if (readsAbortedThisFrame.Contains(buffer)) return false;
            if (!pendingReadBuffers.TryGetValue(buffer, out var request))
            {
                request = new ReadMap { Token = NewToken(), Length = length };
                Api.wgpuBufferAddRef(buffer);
                pendingReadBuffers.Add(buffer, request);
                pendingReadCallbacks[request.Token] = request;
                var callback = WGPUBufferMapCallbackInfo.CreateDefault();
                callback.mode = OperatingSystem.IsBrowser() ? WGPUCallbackMode.AllowSpontaneous : WGPUCallbackMode.AllowProcessEvents;
                callback.callback = OperatingSystem.IsBrowser()
            ? (delegate* unmanaged[Cdecl]<WGPUMapAsyncStatus, WGPUStringView, void*, void*, void>)(delegate* unmanaged[Cdecl]<WGPUMapAsyncStatus, WGPUStringView*, void*, void*, void>)&OnReadMapBrowser
            : &OnReadMap;
                callback.userdata1 = (void*)request.Token;
                Api.wgpuBufferMapAsync(buffer, WGPUMapMode.Read, 0, (nuint)length, callback);
            }
            if (request.Length != length) throw new InvalidOperationException("The pending readback has a different size.");
            ProcessEvents();
            var status = Volatile.Read(ref request.Status);
            if (status == 0) return false;
            if ((WGPUMapAsyncStatus)status != WGPUMapAsyncStatus.Success)
            {
                CancelPendingRead(buffer);
                throw new InvalidOperationException($"WebGPU readback mapping failed ({(WGPUMapAsyncStatus)status}).");
            }
            pointer = (nint)Api.wgpuBufferGetConstMappedRange(buffer, 0, (nuint)length);
            if (pointer == 0) { CancelPendingRead(buffer); throw new InvalidOperationException("WebGPU returned a null mapped range."); }
            pendingReadBuffers.Remove(buffer);
            pendingReadCallbacks.TryRemove(request.Token, out _);
            Api.wgpuBufferRelease(buffer);
            return true;
        }
    }

    // A new GPU copy or disposal invalidates an unconsumed readback. Expiring
    // the token before Unmap makes cancellation callbacks harmless.
    public void CancelPendingRead(WGPUBuffer buffer)
    {
        if (asyncReadBuffers.ContainsKey(buffer)) throw new InvalidOperationException("Await asynchronous readback before reusing this buffer.");
        lock (pendingReadBuffers)
        {
            if (!pendingReadBuffers.Remove(buffer, out var request)) return;
            if (OperatingSystem.IsBrowser() && Volatile.Read(ref request.Status) == 0) readsAbortedThisFrame.Add(buffer);
            pendingReadCallbacks.TryRemove(request.Token, out _);
            Api.wgpuBufferUnmap(buffer);
            Api.wgpuBufferRelease(buffer);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnReadMap(WGPUMapAsyncStatus status, WGPUStringView message, void* user1, void* user2) => HandleReadMap(status, message, user1, user2);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnReadMapBrowser(WGPUMapAsyncStatus status, WGPUStringView* message, void* user1, void* user2) => HandleReadMap(status, *message, user1, user2);

    private static void HandleReadMap(WGPUMapAsyncStatus status, WGPUStringView message, void* user1, void* user2)
    {
        if (pendingReadCallbacks.TryRemove((nint)user1, out var request)) Volatile.Write(ref request.Status, (int)status);
    }
}
#endif
