using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeepSpaceAE.Tests;

/// <summary>Reads PE metadata only; never loads or executes Unity/game/mod assemblies.</summary>
internal static class GamePatchAudit
{
    internal sealed record Finding(string Patch, string Target, string Result, string Detail);
    internal static bool HasPatchableBody(MethodDefinition method) => (method.Attributes & MethodAttributes.Abstract) == 0
        && method.RelativeVirtualAddress != 0 && (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.IL;
    private sealed class Names : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode code) => "System." + code;
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw)
        {
            var type = r.GetTypeDefinition(h); var parent = type.GetDeclaringType();
            return parent.IsNil ? Full(r.GetString(type.Namespace), r.GetString(type.Name)) : GetTypeFromDefinition(r, parent, raw) + "+" + r.GetString(type.Name);
        }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw) => Full(r.GetString(r.GetTypeReference(h).Namespace), r.GetString(r.GetTypeReference(h).Name));
        public string GetTypeFromSpecification(MetadataReader r, object? context, TypeSpecificationHandle h, byte raw) => r.GetTypeSpecification(h).DecodeSignature(this, context);
        public string GetSZArrayType(string element) => element + "[]";
        public string GetArrayType(string element, ArrayShape shape) => element + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string element) => element + "&";
        public string GetPointerType(string element) => element + "*";
        public string GetPinnedType(string element) => element;
        public string GetModifiedType(string modifier, string element, bool required) => element;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericInstantiation(string generic, ImmutableArray<string> arguments) => generic + "<" + string.Join(",", arguments) + ">";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "function-pointer";
        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    }
    private static readonly Names names = new();
    private static string Full(string space, string name) => space.Length == 0 ? name : space + "." + name;
    private static string AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        var ctor = attribute.Constructor;
        var type = ctor.Kind == HandleKind.MemberReference ? reader.GetMemberReference((MemberReferenceHandle)ctor).Parent
            : reader.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType();
        return type.Kind == HandleKind.TypeReference ? names.GetTypeFromReference(reader, (TypeReferenceHandle)type, 0)
            : names.GetTypeFromDefinition(reader, (TypeDefinitionHandle)type, 0);
    }
    internal static IReadOnlyList<Finding> Inspect(string gamePath, string adapterPath)
    {
        using var gameFile = File.OpenRead(gamePath); using var gamePe = new PEReader(gameFile); var game = gamePe.GetMetadataReader();
        using var modFile = File.OpenRead(adapterPath); using var modPe = new PEReader(modFile); var mod = modPe.GetMetadataReader();
        var types = game.TypeDefinitions.ToDictionary(h => names.GetTypeFromDefinition(game, h, 0), StringComparer.Ordinal);
        var results = new List<Finding>();
        foreach (var patchHandle in mod.TypeDefinitions)
        {
            var patch = mod.GetTypeDefinition(patchHandle); string patchName = names.GetTypeFromDefinition(mod, patchHandle, 0);
            foreach (var attrHandle in patch.GetCustomAttributes())
            {
                var attr = mod.GetCustomAttribute(attrHandle);
                if (AttributeName(mod, attr) != "HarmonyLib.HarmonyPatch") continue;
                var args = attr.DecodeValue(names).FixedArguments;
                if (args.Length != 2 || args[0].Value is not string qualified || args[1].Value is not string methodName)
                { results.Add(new(patchName, "?", "FAIL", "Unsupported Harmony target declaration; update this metadata auditor.")); continue; }
                string typeName = qualified.Split(',')[0].Trim(), target = typeName + "." + methodName;
                if (!types.TryGetValue(typeName, out var typeHandle)) { results.Add(new(patchName, target, "FAIL", "Target type missing.")); continue; }
                var methods = game.GetTypeDefinition(typeHandle).GetMethods().Where(h => game.GetString(game.GetMethodDefinition(h).Name) == methodName).ToArray();
                if (methods.Length != 1) { results.Add(new(patchName, target, "FAIL", "Target missing or ambiguous: " + methods.Length)); continue; }
                var method = game.GetMethodDefinition(methods[0]);
                if (!HasPatchableBody(method)) { results.Add(new(patchName, target, "FAIL", "Abstract/runtime/native method has no patchable managed IL body.")); continue; }
                bool valid = true;
                var targetSignature = method.DecodeSignature(names, null);
                var targetParameters = method.GetParameters().Select(h => game.GetParameter(h)).Where(p => p.SequenceNumber > 0)
                    .ToDictionary(p => game.GetString(p.Name), p => p.SequenceNumber, StringComparer.Ordinal);
                foreach (var hookHandle in patch.GetMethods())
                {
                    var hook = mod.GetMethodDefinition(hookHandle);
                    if (!hook.GetCustomAttributes().Any(h => AttributeName(mod, mod.GetCustomAttribute(h)) is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix" or "HarmonyLib.HarmonyFinalizer")) continue;
                    var hookSignature = hook.DecodeSignature(names, null);
                    foreach (var pHandle in hook.GetParameters())
                    {
                        var parameter = mod.GetParameter(pHandle); if (parameter.SequenceNumber == 0) continue;
                        string parameterName = mod.GetString(parameter.Name);
                        string parameterType = hookSignature.ParameterTypes[parameter.SequenceNumber - 1].TrimEnd('&');
                        string? expected = null;
                        if (parameterName.StartsWith("___", StringComparison.Ordinal))
                        {
                            string field = parameterName.Substring(3); var cursor = typeHandle;
                            while (!cursor.IsNil)
                            {
                                var declaration = game.GetTypeDefinition(cursor);
                                foreach (var h in declaration.GetFields())
                                { var f = game.GetFieldDefinition(h); if (game.GetString(f.Name) == field) expected = f.DecodeSignature(names, null); }
                                if (expected != null) break;
                                var parent = declaration.BaseType;
                                cursor = parent.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)parent : default;
                            }
                            if (expected == null) { valid = false; results.Add(new(patchName, target, "FAIL", "Injected field missing: " + field)); }
                        }
                        else if (parameterName == "__result") expected = targetSignature.ReturnType;
                        else if (parameterName.StartsWith("__", StringComparison.Ordinal) && int.TryParse(parameterName.Substring(2), out var ordinal))
                        {
                            if (ordinal < targetSignature.ParameterTypes.Length) expected = targetSignature.ParameterTypes[ordinal];
                            else { valid = false; results.Add(new(patchName, target, "FAIL", "Parameter index out of range: " + parameterName)); }
                        }
                        else if (!parameterName.StartsWith("__", StringComparison.Ordinal))
                        {
                            if (targetParameters.TryGetValue(parameterName, out var sequence)) expected = targetSignature.ParameterTypes[sequence - 1];
                            else { valid = false; results.Add(new(patchName, target, "FAIL", "Named parameter missing: " + parameterName)); }
                        }
                        if (expected != null && expected != parameterType)
                        { valid = false; results.Add(new(patchName, target, "FAIL", $"Injection {parameterName}: expected {expected}, found {parameterType}.")); }
                    }
                }
                if (valid) results.Add(new(patchName, target, "PASS", "Managed body and declared parameter/field bindings present."));
            }
        }
        if (results.Count == 0) results.Add(new("?", "?", "FAIL", "No Harmony declarations audited."));
        return results;
    }
    internal static int Run(string gamePath, string adapterPath, string? reportPath = null)
    {
        var results = Inspect(gamePath, adapterPath);
        foreach (var r in results) Console.WriteLine($"{r.Result} {r.Target} [{r.Patch}]: {r.Detail}");
        Console.WriteLine($"Metadata only: {results.Count(r => r.Result == "PASS")} passed, {results.Count(r => r.Result == "FAIL")} failed. Game/Harmony execution NOT RUN.");
        bool success = results.All(r => r.Result == "PASS");
        if (reportPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
                schema = 1, success,
                gameSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gamePath))).ToLowerInvariant(),
                adapterSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(adapterPath))).ToLowerInvariant(),
                scope = "PE metadata only; no game or Harmony execution", findings = results
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        return success ? 0 : 1;
    }
}
