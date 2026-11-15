// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;

internal static class PixelTolerance
{
    /// <summary>
    /// Whether two 8-bit RGBA pixels differ by at most one step per channel.
    /// </summary>
    /// <remarks>
    /// A color that falls halfway between two 8-bit values, such as 255 * 0.5, is stored as either of them depending on the GPU.
    /// </remarks>
    public static bool SameWithinOne(uint a, uint b)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            if (Math.Abs((int)((a >> shift) & 0xff) - (int)((b >> shift) & 0xff)) > 1)
                return false;
        }
        return true;
    }
}
