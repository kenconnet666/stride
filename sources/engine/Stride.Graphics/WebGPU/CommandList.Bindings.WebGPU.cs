// Copyright (c) .NET Foundation and Contributors.
// Distributed under the MIT license. See LICENSE.md in the project root.
#if STRIDE_GRAPHICS_API_WEBGPU
using System;
using System.Collections.Generic;
using Stride.Graphics.WebGPU;
using Api = Stride.Graphics.WebGPU.WebGPU;

namespace Stride.Graphics;

public unsafe partial class CommandList
{
    private const int BindingCacheLimit = 4096;
    // How many recordings a bind group stays cached without being used.
    private const int BindingCacheLifetime = 16;
    private readonly Dictionary<int, List<CachedBindings>> bindingCache = new();
    private int bindingCacheCount;
    private int bindingCacheRecording;

    private sealed class CachedBindings(WGPUBindGroupLayout layout, WGPUBindGroupEntry[] entries, WGPUBindGroup group, int lastUsed)
    {
        public readonly WGPUBindGroupLayout Layout = layout;
        public readonly WGPUBindGroupEntry[] Entries = entries;
        public readonly WGPUBindGroup Group = group;
        public int LastUsed = lastUsed;
    }

    private static int BindingHash(WGPUBindGroupLayout layout, ReadOnlySpan<WGPUBindGroupEntry> entries)
    {
        var hash = new HashCode();
        hash.Add(layout.Handle);
        foreach (ref readonly var entry in entries)
        {
            hash.Add(entry.binding); hash.Add(entry.buffer.Handle);
            hash.Add(entry.offset); hash.Add(entry.size);
            hash.Add(entry.textureView.Handle); hash.Add(entry.sampler.Handle);
        }
        return hash.ToHashCode();
    }

    private WGPUBindGroup FindBindings(WGPUBindGroupLayout layout, ReadOnlySpan<WGPUBindGroupEntry> entries)
    {
        if (!bindingCache.TryGetValue(BindingHash(layout, entries), out var bucket)) return default;
        foreach (var candidate in bucket)
        {
            if (candidate.Layout != layout || candidate.Entries.Length != entries.Length) continue;
            var equal = true;
            for (var i = 0; i < entries.Length; i++)
            {
                ref readonly var a = ref entries[i];
                ref readonly var b = ref candidate.Entries[i];
                if (a.binding != b.binding || a.buffer != b.buffer || a.offset != b.offset || a.size != b.size
                    || a.textureView != b.textureView || a.sampler != b.sampler)
                { equal = false; break; }
            }
            if (!equal) continue;
            candidate.LastUsed = bindingCacheRecording;
            // Each pass owns a temporary reference, whether created or reused.
            Api.wgpuBindGroupAddRef(candidate.Group);
            return candidate.Group;
        }
        return default;
    }

    private void CacheBindings(WGPUBindGroupLayout layout, ReadOnlySpan<WGPUBindGroupEntry> entries, WGPUBindGroup group)
    {
        if (bindingCacheCount == BindingCacheLimit) ClearBindingCache();
        var hash = BindingHash(layout, entries);
        if (!bindingCache.TryGetValue(hash, out var bucket)) bindingCache.Add(hash, bucket = new());
        var cached = new CachedBindings(layout, entries.ToArray(), group, bindingCacheRecording);
        bucket.Add(cached);
        Api.wgpuBindGroupAddRef(group);
        // The cache compares handles, and it outlives the recording: its references keep a handle from being given to
        // another object after the resource is released.
        Api.wgpuBindGroupLayoutAddRef(layout);
        foreach (ref readonly var entry in cached.Entries.AsSpan())
        {
            if (entry.buffer.Handle != 0) Api.wgpuBufferAddRef(entry.buffer);
            if (entry.textureView.Handle != 0) Api.wgpuTextureViewAddRef(entry.textureView);
            if (entry.sampler.Handle != 0) Api.wgpuSamplerAddRef(entry.sampler);
        }
        bindingCacheCount++;
    }

    private static void ReleaseCachedBindings(CachedBindings cached)
    {
        Api.wgpuBindGroupRelease(cached.Group);
        Api.wgpuBindGroupLayoutRelease(cached.Layout);
        foreach (ref readonly var entry in cached.Entries.AsSpan())
        {
            if (entry.buffer.Handle != 0) Api.wgpuBufferRelease(entry.buffer);
            if (entry.textureView.Handle != 0) Api.wgpuTextureViewRelease(entry.textureView);
            if (entry.sampler.Handle != 0) Api.wgpuSamplerRelease(entry.sampler);
        }
    }

    /// <summary>
    /// Ends a recording for the cache: now and then, the bind groups that the last recordings did not use are released.
    /// </summary>
    private void AgeBindingCache()
    {
        bindingCacheRecording++;
        if (bindingCacheRecording % BindingCacheLifetime != 0) return;
        List<int> emptied = null;
        foreach (var (hash, bucket) in bindingCache)
        {
            for (var i = bucket.Count - 1; i >= 0; i--)
            {
                if (bindingCacheRecording - bucket[i].LastUsed <= BindingCacheLifetime) continue;
                ReleaseCachedBindings(bucket[i]);
                bucket.RemoveAt(i);
                bindingCacheCount--;
            }
            if (bucket.Count == 0) (emptied ??= new()).Add(hash);
        }
        if (emptied != null)
            foreach (var hash in emptied) bindingCache.Remove(hash);
    }

    private void ClearBindingCache()
    {
        foreach (var bucket in bindingCache.Values)
            foreach (var entry in bucket) ReleaseCachedBindings(entry);
        bindingCache.Clear();
        bindingCacheCount = 0;
        // The handle of a released group can name a new one: the open pass gets its group again
        passBindGroup = 0;
    }
}
#endif
