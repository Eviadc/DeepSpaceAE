using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace DeepSpaceAE.Tests;

/// <summary>Runs the installed game's own IL scanner, without launching Unity or loading mod code.</summary>
internal static class NativeModScan
{
    internal static int Run(string managedPath, string manifestPath, string dllDirectory, string outputPath)
    {
        string managed=Path.GetFullPath(managedPath), manifest=Path.GetFullPath(manifestPath);
        string binaries=Path.GetFullPath(dllDirectory), output=Path.GetFullPath(outputPath);
        var xml=XDocument.Load(manifest).Root ?? throw new InvalidDataException("Missing manifest root.");
        var names=xml.Element("Assemblies")?.Elements("Assembly").Select(e=>e.Value).ToArray()
            ?? throw new InvalidDataException("Missing assembly list.");
        if(names.Length==0 || names.Any(n=>n!=Path.GetFileName(n) || !n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Expected listed DLL filenames.");
        var inputs=names.Select(n=>Path.Combine(binaries,n)).ToArray();
        var declared=xml.Element("Capabilities")?.Elements("Capability").Select(e=>e.Value).ToArray() ?? Array.Empty<string>();
        string[] retiring={"reflection-external","native","dynamic-code","threads"};
        Assembly? Resolve(AssemblyLoadContext _,AssemblyName name)
        {
            string path=Path.Combine(managed,name.Name+".dll");
            return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
        }
        AssemblyLoadContext.Default.Resolving+=Resolve;
        try
        {
            string gamePath=Path.Combine(managed,"Assembly-CSharp.dll");
            var game=AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
            // Unity's ModLoader.Awake normally collects this exact folder inventory.
            // Supply it for reference checks in this offline host. Do not alter scanner flags.
            var gameReferences=Directory.GetFiles(managed,"*.dll",SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n).ToArray();
            var scanner=game.GetType("ModScanner",true)!;
            var native=scanner.GetMethod("Scan")!.Invoke(null,new object?[]{inputs,gameReferences,
                binaries==Path.GetDirectoryName(manifest)?binaries:null,true})!;
            var options=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
            var scan=JsonSerializer.SerializeToElement(native,native.GetType(),options);
            var detected=scan.GetProperty("Capabilities").EnumerateArray().Select(e=>e.GetString()!).ToArray();
            bool success=!scan.GetProperty("Refused").GetBoolean()
                && !detected.Concat(declared).Intersect(retiring,StringComparer.Ordinal).Any()
                && scan.GetProperty("Pending").GetArrayLength()==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output,JsonSerializer.Serialize(new {
                success, execution="Installed native IL scanner; no Unity gameplay or mod execution",
                referenceInventoryNote="Managed DLL names substitute for ModLoader.Awake inventory; capability policy is unchanged",
                gameSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(gamePath))),
                manifestSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest))),
                dllSha256=inputs.ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))),
                declared,gameReferences,scan
            },options)+Environment.NewLine);
            Console.WriteLine($"Native mod scan: {(success?"PASS":"FAIL")}; methods={scan.GetProperty("MethodsScanned")}; findings={scan.GetProperty("Findings").GetArrayLength()}; report={output}");
            return success?0:1;
        }
        finally { AssemblyLoadContext.Default.Resolving-=Resolve; }
    }
}
