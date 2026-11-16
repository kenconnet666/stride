using System.Security.Cryptography;
using System.Text.Json;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Core.Storage;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering.Compositing;
using System.Diagnostics;
using Stride.Shaders;
using Stride.Core.Serialization;
using Stride.Shaders.Compiler;
using Stride.Rendering;
using System.Reflection;
using System.Runtime.CompilerServices;

// --replay-effects <database> <requests> <game assembly> [engine checkout]
if (args.Length is 4 or 5 && args[0] == "--replay-effects")
{
    var assemblyPath = Path.GetFullPath(args[3]);
    // A game built from packages has none of its dependencies next to it: they are those of the engine checkout
    // its packages redirect to, each in the output folder of the project of its name.
    var checkoutSources = args.Length == 5 ? Path.Combine(Path.GetFullPath(args[4]), "sources") : null;
    // The other libraries of the game are in the NuGet packages its dependency file names
    var packageLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var dependencyFile = Path.ChangeExtension(assemblyPath, ".deps.json");
    if (File.Exists(dependencyFile))
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        using var dependencies = JsonDocument.Parse(File.ReadAllBytes(dependencyFile));
        var libraries = dependencies.RootElement.GetProperty("libraries");
        foreach (var target in dependencies.RootElement.GetProperty("targets").EnumerateObject())
        foreach (var library in target.Value.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("runtime", out var runtime) || !libraries.TryGetProperty(library.Name, out var entry)
                || !entry.TryGetProperty("path", out var packagePath)) continue;
            foreach (var file in runtime.EnumerateObject())
                packageLibraries.TryAdd(Path.GetFileNameWithoutExtension(file.Name), Path.Combine(packages, packagePath.GetString()!, file.Name));
        }
    }
    Assembly? ResolveSample(object? sender, ResolveEventArgs request)
    {
        var name = new AssemblyName(request.Name).Name!;
        var path = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name + ".dll");
        if (!File.Exists(path) && checkoutSources != null)
        {
            path = Directory.EnumerateDirectories(checkoutSources, name, SearchOption.AllDirectories)
                .Select(project => Path.Combine(project, "bin", "Release", "net11.0", name + ".dll"))
                .FirstOrDefault(File.Exists) ?? path;
        }
        if (!File.Exists(path) && packageLibraries.TryGetValue(name, out var packaged)) path = packaged;
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
    AppDomain.CurrentDomain.AssemblyResolve += ResolveSample;
    try
    {
        RuntimeHelpers.RunModuleConstructor(Assembly.LoadFrom(assemblyPath).ManifestModule.ModuleHandle);
        using var mount = new FileSystemProvider("/browser-replay", Path.GetFullPath(args[1]));
        using var database = new ObjectDatabase("/browser-replay", "index");
        using var provider = new DatabaseFileProvider(database);
        using var compiler = new EffectCompiler(provider) { ValidateSpirv = true };
        compiler.SourceDirectories.Add("shaders");
        using var requests = JsonDocument.Parse(File.ReadAllBytes(args[2]));
        foreach (var request in requests.RootElement.EnumerateArray())
        {
            using var bytes = new MemoryStream(Convert.FromBase64String(request.GetProperty("payload").GetString()!));
            var reader = new BinarySerializationReader(bytes);
            if (reader.ReadInt32() != 1) throw new InvalidDataException("Unknown browser effect request version.");
            var mixin = reader.Read<ShaderMixinSource>();
            var parameters = reader.Read<EffectCompilerParameters>();
            var compilerParameters = reader.Read<CompilerParameters>();
            compilerParameters.EffectParameters = parameters;
            var recordedHash = reader.Read<ObjectId>();
            if (bytes.Position != bytes.Length || recordedHash.ToString() != request.GetProperty("hash").GetString())
                throw new InvalidDataException("Recorded effect request is malformed: its payload and hash do not match.");
            if (parameters.Platform != GraphicsPlatform.WebGPU)
                throw new InvalidDataException($"Recorded effect request is for {parameters.Platform}, not WebGPU.");
            // The runtime looks the bytecode up by the id it computes from the same mixin: an id recorded by an older
            // compiler is only reported.
            var requestId = ShaderMixinObjectId.Compute(mixin, parameters);
            if (requestId != recordedHash)
                Console.WriteLine($"Effect id of {mixin.Name} is now {requestId} (recorded as {recordedHash}).");
            var result = compiler.Compile(mixin, parameters, compilerParameters, requestId).WaitForResult();
            if (result.CompilationLog.HasErrors || result.Bytecode == null)
                throw new InvalidDataException(string.Join(Environment.NewLine, result.CompilationLog.Messages.Select(message => message.Text)));
            using var output = new MemoryStream();
            result.Bytecode.WriteTo(output);
            new BinarySerializationWriter(output).Write((int)EffectBytecodeCacheLoadSource.StartupCache);
            output.Position = 0;
            var id = result.Bytecode.ComputeId();
            database.Write(output, id, true);
            database.ContentIndexMap["__shaders_bytecode__/" + requestId] = id;
            Console.WriteLine($"Replayed {requestId}:{mixin.Name}, {parameters.Profile}, debug={parameters.Debug}, optimization={parameters.OptimizationLevel}, stages={result.Bytecode.Stages.Length}.");
        }
    }
    finally { AppDomain.CurrentDomain.AssemblyResolve -= ResolveSample; }
    return;
}

if (args.Length == 5 && args[0] == "--webgpu-cache")
{
    // Convert a separate browser database. Never mutate desktop assets/cache keys.
    var sourceRoot = Path.GetFullPath(args[1]);
    var outputRoot = Path.GetFullPath(args[2]);
    if (Directory.Exists(outputRoot)) throw new IOException("Browser database output must be new.");
    var naga = Path.GetFullPath(args[3]);
    var converter = Path.GetFullPath(args[4]);
    Directory.CreateDirectory(outputRoot);
    using var sourceMount = new FileSystemProvider("/browser-source", sourceRoot);
    using var outputMount = new FileSystemProvider("/browser-output", outputRoot);
    using var sourceDb = new ObjectDatabase("/browser-source", "index");
    using var outputDb = new ObjectDatabase("/browser-output", "index", loadDefaultBundle: false);
    var index = sourceDb.ContentIndexMap.GetMergedIdMap().ToDictionary();
    var objectIds = sourceDb.EnumerateObjects().Concat(index.Values).ToHashSet();
    foreach (var id in objectIds)
    {
        using var stream = sourceDb.OpenStream(id);
        using var unpacked = UnpackChunk(stream);
        outputDb.Write(unpacked, id, true);
    }
    var effects = index.Where(pair => pair.Key.StartsWith("__shaders_bytecode__/", StringComparison.Ordinal)).ToArray();
    // A sample without effects of its own has none until the effects a browser run asked for are replayed
    if (effects.Length == 0) Console.WriteLine("The sample database has no precompiled effect: record the effect requests of a browser run, then prepare the content again.");
    var temporary = Directory.CreateTempSubdirectory("stride-browser-effects-");
    try
    {
        var convertedIds = new Dictionary<ObjectId, ObjectId>();
        foreach (var pair in effects)
        {
            if (!convertedIds.TryGetValue(pair.Value, out var newId))
            {
                using var sourceStream = sourceDb.OpenStream(pair.Value);
                var effect = EffectBytecode.FromStream(sourceStream);
                Console.WriteLine($"Preparing {pair.Key}: {effect.Stages.Length} stage(s).");
                if (effect.Stages.Length == 0) throw new InvalidDataException("Cached effect has no executable shader stages.");
                // Preserve the cache source marker following the bytecode record.
                using var trailer = new MemoryStream();
                sourceStream.CopyTo(trailer);
                var input = Path.Combine(temporary.FullName, "input.effect");
                var output = Path.Combine(temporary.FullName, "output.effect");
                using (var stream = File.Create(input)) effect.WriteTo(stream);
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { converter, naga, input, output }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Cannot start shader converter.");
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidDataException($"WGSL conversion failed for {pair.Key} ({pair.Value}).");
                using var converted = File.OpenRead(output);
                var portable = EffectBytecode.FromStream(converted);
                if (portable.Stages.Any(stage => !WebGpuShaderPackage.IsPackage(stage.Data)))
                    throw new InvalidDataException("Converter returned a stage without a WGSL package.");
                using var bytes = new MemoryStream();
                portable.WriteTo(bytes);
                trailer.Position = 0;
                trailer.CopyTo(bytes);
                newId = ObjectId.FromBytes(bytes.ToArray());
                bytes.Position = 0;
                outputDb.Write(bytes, newId, true);
                objectIds.Add(newId);
                convertedIds.Add(pair.Value, newId);
            }
            index[pair.Key] = newId;
        }
        outputDb.CreateBundle(objectIds.ToArray(), "default", outputDb.BundleBackend,
            new HashSet<ObjectId>(), index, [], useIncrementalBundle: false);
        foreach (var alias in Directory.GetFiles(sourceRoot, "*.aliases").Concat(
            File.Exists(Path.Combine(sourceRoot, "aliases")) ? new[] { Path.Combine(sourceRoot, "aliases") } : []))
            File.Copy(alias, Path.Combine(outputRoot, Path.GetFileName(alias)));
        Console.WriteLine($"Prepared browser cache: {effects.Length} keys, {convertedIds.Count} unique effects, {objectIds.Count} objects.");
    }
    finally { temporary.Delete(recursive: true); }
    return;
}

static MemoryStream UnpackChunk(Stream source)
{
    // Bundle storage moves references before the object for streaming. CreateBundle
    // expects loose-object order; restore that order before bundling a second time.
    using var input = new MemoryStream();
    source.CopyTo(input);
    input.Position = 0;
    var header = ChunkHeader.Read(new BinarySerializationReader(input));
    var output = new MemoryStream();
    if (header == null || header.OffsetToReferences >= header.OffsetToObject)
    {
        input.Position = 0;
        input.CopyTo(output);
    }
    else
    {
        var references = new byte[checked(header.OffsetToObject - header.OffsetToReferences)];
        input.Position = header.OffsetToReferences;
        input.ReadExactly(references);
        header.Write(new BinarySerializationWriter(output));
        var objectOffset = output.Position;
        input.Position = header.OffsetToObject;
        input.CopyTo(output);
        header.OffsetToReferences = checked((int)output.Position);
        output.Write(references);
        header.OffsetToObject = checked((int)objectOffset);
        output.Position = 0;
        header.Write(new BinarySerializationWriter(output));
    }
    output.Position = 0;
    return output;
}

if (args.Length == 2 && args[0] == "--fixture")
{
    var root = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(root);
    using var mount = new FileSystemProvider("/browser-fixture", root);
    using var database = new ObjectDatabase("/browser-fixture", "index", loadDefaultBundle: false);
    using var provider = new DatabaseFileProvider(database);
    var content = new ContentManager(new DatabaseFileProviderService(provider));
    using var scene = new Scene();
    scene.Entities.Add(new Entity("Browser asset marker", new Vector3(1, 2, 3)));
    using var compositor = new GraphicsCompositor();
    var settings = new GameSettings
    {
        PackageName = "BrowserFixture",
        DefaultSceneUrl = "/BrowserFixture/MainScene",
        DefaultGraphicsCompositorUrl = "/Stride.Engine/GraphicsCompositor",
    };
    content.Save(settings.DefaultSceneUrl, scene);
    content.Save(settings.DefaultGraphicsCompositorUrl, compositor);
    content.Save(GameSettings.AssetUrl, settings);
    var index = database.ContentIndexMap.GetMergedIdMap().ToDictionary();
    database.CreateBundle(index.Values.Distinct().ToArray(), "default", database.BundleBackend,
        new HashSet<ObjectId>(), index, [], useIncrementalBundle: false);
    File.WriteAllText(Path.Combine(root, "aliases"), "MainScene|/BrowserFixture/MainScene\n");
    Console.WriteLine($"Created {index.Count} actual serialized fixture assets.");
    return;
}
if (args.Length is < 2 or > 3)
    throw new ArgumentException("Usage: ContentPackageTool <packaged data/db> <web output> [default bundle], or --fixture <db output>");
var source = Path.GetFullPath(args[0]);
var destination = Path.GetFullPath(args[1]);
if (source == destination || Path.GetRelativePath(source, destination).Split(Path.DirectorySeparatorChar)[0] != "..")
    throw new ArgumentException("Web output must be outside the source database.");
var bundle = args.Length == 3 ? args[2] : "default";
if (bundle.Length == 0 || bundle.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
    throw new ArgumentException("Default bundle must be a portable name.");
var files = Directory.GetFiles(Path.Combine(source, "bundles"), "*.bundle").ToList();
if (!File.Exists(Path.Combine(source, "index"))) throw new FileNotFoundException("Database index is absent.");
files.Add(Path.Combine(source, "index"));
if (!files.Contains(Path.Combine(source, "bundles", bundle + ".bundle")))
    throw new FileNotFoundException("Default bundle is absent.");
files.AddRange(Directory.GetFiles(source, "*.aliases"));
if (File.Exists(Path.Combine(source, "aliases"))) files.Add(Path.Combine(source, "aliases"));
Directory.CreateDirectory(Path.Combine(destination, "bundles"));
var manifest = new BrowserContentManifest { Version = 1, DefaultBundle = bundle };
foreach (var file in files.Order(StringComparer.Ordinal))
{
    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
    var bytes = File.ReadAllBytes(file);
    File.WriteAllBytes(Path.Combine(destination, relative), bytes);
    manifest.Files.Add(new BrowserContentFile { Path = relative, Size = bytes.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
}
File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonSerializer.Serialize(manifest, BrowserContentJson.Default.BrowserContentManifest));
Console.WriteLine($"Packaged {manifest.Files.Count} files, {manifest.Files.Sum(file => file.Size)} bytes.");
