// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

// Stands in for the Mono AOT compiler of a WebAssembly publish (mono-aot-cross --response=<file>) and keeps what it
// makes. The MonoAOTCompiler task runs the compiler for every assembly as soon as one changed; the compiler is then most
// of the time of a publish. This tool gives back the outputs of an earlier run when the assembly, the assemblies it
// references (all the way down) and the options are the same, and runs the compiler otherwise.
//
// The real compiler is named by the environment variable STRIDE_MONO_AOT_CROSS, or by the file mono-aot-cross.path next
// to this tool. The outputs are kept in %LOCALAPPDATA%\Stride\MonoAotCache, shared by the games: the assemblies of .NET
// and of other packages are the same for all of them when they are not trimmed.

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

const string Version = "1";

var compiler = Environment.GetEnvironmentVariable("STRIDE_MONO_AOT_CROSS");
if (string.IsNullOrEmpty(compiler))
{
    var pathFile = Path.Combine(AppContext.BaseDirectory, "mono-aot-cross.path");
    compiler = File.Exists(pathFile) ? File.ReadAllText(pathFile).Trim() : null;
}
if (string.IsNullOrEmpty(compiler) || !File.Exists(compiler))
{
    Console.Error.WriteLine("MonoAotCache: the Mono AOT compiler is not found (STRIDE_MONO_AOT_CROSS or mono-aot-cross.path).");
    return 1;
}

// Anything but one response file (the icall table, a dedup run of several assemblies) goes to the compiler as it is
if (args.Length != 1 || !args[0].StartsWith("--response=", StringComparison.Ordinal))
    return RunCompiler();
var tokens = SplitArguments(File.ReadAllText(args[0]["--response=".Length..].Trim('"')));
var aotIndex = tokens.FindIndex(x => x.StartsWith("--aot=", StringComparison.Ordinal));
if (aotIndex < 0 || aotIndex != tokens.Count - 2)
    return RunCompiler();

var assembly = Path.GetFullPath(tokens[^1]);
var options = tokens[aotIndex]["--aot=".Length..].Split(',');
// The outputs (outfile=, llvm-outfile=, data-outfile=...) and the temporary folders are paths of this run: they are not
// part of the key, the other options are
var outputs = new List<(string Name, string Path)>();
var key = new StringBuilder($"{Version}\n{compiler}\n{File.GetLastWriteTimeUtc(compiler):O}\n");
foreach (var option in options)
{
    var equals = option.IndexOf('=');
    var name = equals < 0 ? option : option[..equals];
    if (name.EndsWith("outfile", StringComparison.Ordinal))
        outputs.Add((name, option[(equals + 1)..]));
    else if (name is not ("temp-path" or "msym-dir"))
        key.Append(option).Append('\n');
}
foreach (var token in tokens.Take(aotIndex))
    key.Append(token).Append('\n');
if (outputs.Count == 0)
    return RunCompiler();

var searchFolders = new List<string> { Path.GetDirectoryName(assembly)! };
searchFolders.AddRange((Environment.GetEnvironmentVariable("MONO_PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
foreach (var (name, hash) in ReferenceClosure(assembly, searchFolders))
    key.Append(name).Append(' ').Append(hash).Append('\n');

var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stride", "MonoAotCache", id);
if (File.Exists(Path.Combine(cache, "complete")))
{
    foreach (var (name, path) in outputs)
    {
        var kept = Path.Combine(cache, name);
        if (File.Exists(kept))
            File.Copy(kept, path, overwrite: true);
    }
    return 0;
}

var code = RunCompiler();
if (code != 0)
    return code;
try
{
    // Kept in a folder of its own, then renamed: a run that reads the cache sees all the outputs or none
    var work = cache + "." + Environment.ProcessId;
    Directory.CreateDirectory(work);
    foreach (var (name, path) in outputs)
        if (File.Exists(path))
            File.Copy(path, Path.Combine(work, name), overwrite: true);
    File.WriteAllText(Path.Combine(work, "complete"), key.ToString());
    if (Directory.Exists(cache))
        Directory.Delete(work, recursive: true);
    else
        Directory.Move(work, cache);
}
catch (IOException error)
{
    // Another publish kept the same outputs at the same time: the compile itself succeeded
    Console.Error.WriteLine($"MonoAotCache: the outputs of {Path.GetFileName(assembly)} were not kept ({error.Message}).");
}
return 0;

int RunCompiler()
{
    var start = new ProcessStartInfo(compiler) { UseShellExecute = false };
    foreach (var argument in args)
        start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    process.WaitForExit();
    return process.ExitCode;
}

// The hashes of an assembly and of the assemblies it references, all the way down, by name: what the compiler reads to
// make its code (layouts of value types, inlined methods, generic instances)
static IEnumerable<(string Name, string Hash)> ReferenceClosure(string assembly, List<string> searchFolders)
{
    var seen = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var pending = new Stack<string>();
    pending.Push(assembly);
    seen[Path.GetFileNameWithoutExtension(assembly)] = string.Empty;
    while (pending.Count > 0)
    {
        var path = pending.Pop();
        var name = Path.GetFileNameWithoutExtension(path);
        var bytes = File.ReadAllBytes(path);
        seen[name] = Convert.ToHexString(SHA256.HashData(bytes));
        using var reader = new PEReader(new MemoryStream(bytes));
        if (!reader.HasMetadata)
            continue;
        var metadata = reader.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (seen.ContainsKey(reference))
                continue;
            var found = searchFolders.Select(folder => Path.Combine(folder, reference + ".dll")).FirstOrDefault(File.Exists);
            // A reference that is not there (trimmed away) still names the key
            seen[reference] = found == null ? "missing" : string.Empty;
            if (found != null)
                pending.Push(found);
        }
    }
    return seen.Select(x => (x.Key, x.Value));
}

// The arguments of a response file: separated by spaces, a quoted one keeps its spaces
static List<string> SplitArguments(string text)
{
    var result = new List<string>();
    var current = new StringBuilder();
    var quoted = false;
    var any = false;
    foreach (var c in text)
    {
        if (c == '"')
        {
            quoted = !quoted;
            any = true;
        }
        else if (char.IsWhiteSpace(c) && !quoted)
        {
            if (any)
                result.Add(current.ToString());
            current.Clear();
            any = false;
        }
        else
        {
            current.Append(c);
            any = true;
        }
    }
    if (any)
        result.Add(current.ToString());
    return result;
}
