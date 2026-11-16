// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

#pragma warning disable 436 // Stride.PublicKeys is defined in multiple assemblies

[assembly: InternalsVisibleTo("Stride.Engine" + Stride.PublicKeys.Default)]

[assembly: UnsupportedOSPlatform("browser", "Bullet physics is a native library with no WebAssembly build: use Stride.BepuPhysics.")]
