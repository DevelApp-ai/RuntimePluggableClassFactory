using DevelApp.RuntimePluggableClassFactory.Security;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 4 (issue #28): security validator escape rate.
    ///
    /// An adversarial plugin corpus is defined as plain C# source in this
    /// file and compiled at run time with Roslyn into one DLL per corpus
    /// item. The corpus source is committed (MIT, like the rest of the
    /// harness) and is also written verbatim into the measurement output
    /// directory, so every emitted corpus DLL can be reproduced bit-for-bit
    /// from committed source. No binary assets are committed.
    ///
    /// Each item is evaluated against the three shipped policy levels
    /// (Default / Strict / Permissive) with DefaultPluginSecurityValidator.
    ///
    /// Definitions used by this measurement:
    /// - "malicious"  = the corpus item's documented intent is a capability
    ///                  a plugin must not have (process spawn, file write,
    ///                  socket backdoor, dynamic assembly loading,
    ///                  prohibited namespace/base-type misuse, suspicious
    ///                  attributes).
    /// - "escape"     = the item is malicious AND the validator returns
    ///                  IsValid = true for the policy level.
    /// - "verified escape" = the escaped item was actually loaded and its
    ///                  payload executed, producing the forbidden effect in
    ///                  the harness sandbox directory (proves the escape is
    ///                  a real capability, not a theoretical one).
    ///
    /// Payloads are safe: they write a marker file into a harness-owned
    /// subdirectory only (or spawn "/bin/touch &lt;marker&gt;" on Linux).
    /// No network, no destructive operations.
    /// </summary>
    public static class M4SecurityEscapeRate
    {
        public static async Task<Dictionary<string, object>> RunAsync(string outputDir)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m4-security-escape-rate",
                ["description"] = "Adversarial plugin corpus (compiled at run time from committed C# source) evaluated against Default/Strict/Permissive security policies; per-level escape rates with issue-code detail and payload verification",
                ["corpusLicense"] = "MIT (harness source, this repository)",
                ["corpusCompiler"] = $"Roslyn {typeof(CSharpCompilation).Assembly.GetName().Version} (C#, run-time compilation of committed corpus source)"
            };

            var sandboxDir = Path.Combine(outputDir, "m4-sandbox");
            Directory.CreateDirectory(sandboxDir);

            var corpus = BuildCorpus(sandboxDir);
            var items = new List<Dictionary<string, object>>();
            var policies = new (string Name, Func<PluginSecuritySettings> Factory)[]
            {
                ("Default", PluginSecuritySettings.CreateDefault),
                ("Strict", PluginSecuritySettings.CreateStrict),
                ("Permissive", PluginSecuritySettings.CreatePermissive)
            };

            foreach (var item in corpus)
            {
                var entry = new Dictionary<string, object>
                {
                    ["id"] = item.Id,
                    ["intent"] = item.Intent,
                    ["malicious"] = item.Malicious,
                    ["description"] = item.Description,
                    ["assembly"] = item.AssemblyPath != null ? Path.GetFileName(item.AssemblyPath) : null,
                    ["buildSucceeded"] = item.AssemblyPath != null,
                    ["sourceFile"] = item.SourceFile != null ? Path.GetFileName(item.SourceFile) : null
                };

                if (item.AssemblyPath == null)
                {
                    items.Add(entry);
                    continue;
                }

                foreach (var (policyName, policyFactory) in policies)
                {
                    Console.WriteLine($"[m4] validating {item.Id} against {policyName} ...");
                    var validator = new DefaultPluginSecurityValidator(policyFactory());
                    var result = await validator.ValidateAssemblyAsync(item.AssemblyPath);
                    var escaped = item.Malicious && result.IsValid;

                    entry[$"{policyName}_isValid"] = result.IsValid;
                    entry[$"{policyName}_riskLevel"] = result.RiskLevel.ToString();
                    entry[$"{policyName}_issueCodes"] = result.Issues.Select(i => i.Code).ToArray();
                    entry[$"{policyName}_warningCodes"] = result.Warnings.Select(w => w.Code).Distinct().ToArray();
                    entry[$"{policyName}_escaped"] = escaped;
                }

                // Payload verification: only meaningful when the item escaped
                // at Default and defines a payload method.
                var escapedDefault = (bool)entry["Default_escaped"];
                if (escapedDefault && item.PayloadMethodName != null)
                {
                    var (executed, markerObserved, note) = await ExecutePayload(
                        item.AssemblyPath, item.PayloadMethodName, item.PayloadMarkerPath!);
                    entry["payloadVerification"] = new Dictionary<string, object>
                    {
                        ["payload"] = item.Id,
                        ["executed"] = executed,
                        ["forbiddenEffectObserved"] = markerObserved,
                        ["note"] = note
                    };
                }

                items.Add(entry);
            }

            report["items"] = items;

            // Per-level escape rates over malicious items (built ones only)
            var evaluatedItems = items.Where(i => (bool)i["buildSucceeded"]).ToList();
            var maliciousCount = evaluatedItems.Count(i => (bool)i["malicious"]);
            var rates = new Dictionary<string, object>();
            foreach (var (policyName, _) in policies)
            {
                var escapedIds = evaluatedItems.Where(i => (bool)i[$"{policyName}_escaped"])
                    .Select(i => i["id"]).ToArray();
                rates[policyName] = new Dictionary<string, object>
                {
                    ["maliciousItems"] = maliciousCount,
                    ["escaped"] = escapedIds.Length,
                    ["escapeRatePercent"] = maliciousCount == 0 ? 0 : Math.Round(escapedIds.Length * 100.0 / maliciousCount, 1),
                    ["escapedIds"] = escapedIds
                };
            }
            report["escapeRates"] = rates;
            report["benignControlValidDefault"] = evaluatedItems
                .Where(i => !(bool)i["malicious"])
                .All(i => (bool)i["Default_isValid"]);

            return report;
        }

        // ----------------------------------------------------------------------
        // Corpus definition (plain C# source; MARKER_* placeholders are
        // substituted with sandbox paths before compilation)
        // ----------------------------------------------------------------------

        private sealed record CorpusItem(
            string Id,
            string Intent,
            string Description,
            bool Malicious,
            string Source,
            string? PayloadMethodName = null,
            string? PayloadMarkerPath = null)
        {
            public string? AssemblyPath { get; set; }
            public string? SourceFile { get; set; }
        }

        private static List<CorpusItem> BuildCorpus(string sandboxDir)
        {
            var processMarker = Path.Combine(sandboxDir, "marker-process.txt");
            var fileMarker = Path.Combine(sandboxDir, "marker-filestream.txt");

            var corpus = new List<CorpusItem>
            {
                new(
                    "benign-wordguesser",
                    "none (control)",
                    "Plain plugin type with no prohibited shapes. Sanity control: must be valid at every level; if this is blocked, the corpus or validator is misconfigured.",
                    false,
                    @"namespace Benign
{
    public class WordGuesser
    {
        public bool Guess(string word) => true;
    }
}"),

                new(
                    "prohibited-namespace-type",
                    "declares a type in a prohibited namespace (System.Diagnostics)",
                    "The validator's namespace check inspects the DECLARED type's namespace. A type declared inside System.Diagnostics is caught by SEC202 (High) and must be blocked at Default and Strict; Permissive removes prohibited namespaces and lets it through by design.",
                    true,
                    @"namespace System.Diagnostics
{
    public class EvilDeclaredHere
    {
        public void Harmless() { }
    }
}"),

                new(
                    "process-inheritor",
                    "inherits from the prohibited base type System.Diagnostics.Process",
                    "Exercises SEC201 (prohibited base types). Expected: blocked at Default (and Strict); Permissive clears ProhibitedBaseTypes by design.",
                    true,
                    @"namespace Adversarial
{
    public class ProcessInheritor : System.Diagnostics.Process
    {
        public void Spawn() { }
    }
}"),

                new(
                    "process-spawner",
                    "spawns a process from a method body via Process.Start",
                    "The validator inspects metadata (names, namespaces, base types, field types), not IL bodies. A benignly-named method whose body calls Process.Start has no prohibited metadata shape and is expected to escape every level. Payload: spawns '/bin/touch MARKER' (Linux) so an escape is verifiable as a real capability.",
                    true,
                    @"namespace Adversarial
{
    public class ProcessSpawner
    {
        public void Run()
        {
            System.Diagnostics.Process.Start(""/bin/touch"", ""MARKER_PROCESS"");
        }
    }
}",
                    PayloadMethodName: "Run",
                    PayloadMarkerPath: processMarker),

                new(
                    "filestream-field",
                    "holds a prohibited FileStream field and writes a file from a method body",
                    "Exercises SEC501: a FileStream field is a prohibited FIELD type, but SEC501 severity is Medium, which does NOT block loading (only High-severity issues invalidate). Expected: escapes every level with a recorded issue. Payload: File.Create(MARKER).Dispose() executed after escaping Default.",
                    true,
                    @"using System.IO;

namespace Adversarial
{
    public class FileStreamField
    {
        public FileStream Backdoor;

        public void Run()
        {
            File.Create(""MARKER_FILESTREAM"").Dispose();
        }
    }
}",
                    PayloadMethodName: "Run",
                    PayloadMarkerPath: fileMarker),

                new(
                    "socket-backdoor",
                    "holds a System.Net.Sockets.Socket field (prohibited capability, non-prohibited field type)",
                    "The prohibited field-type list covers Process/FileStream/NetworkStream but NOT Socket. A Socket-typed field with a benign namespace and no prohibited base type has no blocking metadata shape: expected to escape every level, including Strict (Strict adds prohibited namespaces for DECLARED types only). Documents a real gap per policy level. No payload executed (sockets would require network access; the documented intent is the capability itself).",
                    true,
                    @"using System.Net.Sockets;

namespace Adversarial
{
    public class SocketBackdoor
    {
        public Socket Channel;

        public void Connect() { }
    }
}"),

                new(
                    "assembly-reloader",
                    "loads an arbitrary assembly path from a method body via Assembly.LoadFrom",
                    "Method-name heuristics only produce WARNINGS (and only when the method's own name matches the pattern); a method named 'Run' calling Assembly.LoadFrom is invisible to metadata validation. Expected: escapes every level. Documents that dynamic assembly loading is not blocked at any policy level.",
                    true,
                    @"namespace Adversarial
{
    public class AssemblyReloader
    {
        public void Run()
        {
            System.Reflection.Assembly.LoadFrom(""MARKER_ASSEMBLY_LOAD"");
        }
    }
}"),

                new(
                    "suspicious-attributes",
                    "carries a security-named assembly attribute (SecurityPermissionAttribute mimic)",
                    "The attribute check (SEC301) fires a WARNING for security-named attributes but never blocks. Expected: escapes every level with SEC301 warnings recorded. Documents the warning-only semantics of attribute inspection.",
                    true,
                    @"[assembly: Adversarial.SecurityPermissionAttribute]

namespace Adversarial
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
    public sealed class SecurityPermissionAttribute : System.Attribute
    {
    }
}")
            };

            // Substitute sandbox marker paths so the payloads are fully
            // reproducible from the written source files.
            foreach (var item in corpus)
            {
                var source = item.Source
                    .Replace("MARKER_PROCESS", processMarker)
                    .Replace("MARKER_FILESTREAM", fileMarker)
                    .Replace("MARKER_ASSEMBLY_LOAD", Path.Combine(sandboxDir, "not-a-real-plugin.dll"));

                var sourceFile = Path.Combine(sandboxDir, item.Id + ".cs");
                File.WriteAllText(sourceFile, source, Encoding.UTF8);
                item.SourceFile = sourceFile;

                try
                {
                    item.AssemblyPath = CompileCorpusItem(item.Id, sourceFile, sandboxDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[m4] corpus build FAILED for {item.Id}: {ex.GetType().Name}: {ex.Message}");
                    item.AssemblyPath = null;
                }
            }
            return corpus;
        }

        // ----------------------------------------------------------------------
        // Corpus compilation (Roslyn, references = the running BCL, cached once)
        // ----------------------------------------------------------------------

        private static readonly Lazy<IEnumerable<MetadataReference>> BclReferences = new(() =>
        {
            var trustedPlatformAssemblies =
                ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not set; cannot resolve BCL references for corpus compilation.");
            return trustedPlatformAssemblies.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        }, isThreadSafe: false);

        private static string CompileCorpusItem(string id, string sourceFile, string sandboxDir)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName: "Adversarial." + id,
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile) },
                references: BclReferences.Value,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithPlatform(Platform.AnyCpu));

            var dllPath = Path.Combine(sandboxDir, id + ".dll");
            using var dllStream = new MemoryStream();
            var result = compilation.Emit(dllPath);
            if (!result.Success)
            {
                var diagnostics = string.Join("; ",
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"{d.Id}: {d.GetMessage()}"));
                throw new InvalidOperationException($"Roslyn compilation failed for {id}: {diagnostics}");
            }

            return dllPath;
        }

        // ----------------------------------------------------------------------
        // Payload verification
        // ----------------------------------------------------------------------

        private static async Task<(bool executed, bool markerObserved, string note)> ExecutePayload(
            string assemblyPath, string methodName, string markerPath)
        {
            try
            {
                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }

                var assembly = Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetTypes().First(t => t.GetMethod(methodName) != null);
                var instance = Activator.CreateInstance(type);
                type.GetMethod(methodName)!.Invoke(instance, null);

                // The process-spawn payload runs asynchronously (a spawned
                // child writes the marker); poll briefly so the check does
                // not race the child.
                var deadline = DateTime.UtcNow.AddSeconds(2);
                var markerObserved = File.Exists(markerPath);
                while (!markerObserved && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                    markerObserved = File.Exists(markerPath);
                }
                return (true, markerObserved,
                    markerObserved
                        ? "Escaped plugin was loaded and executed; the forbidden effect (marker file) occurred."
                        : "Escaped plugin was loaded and executed; marker not observed (payload not effective on this OS/configuration - see description).");
            }
            catch (Exception ex)
            {
                return (false, false, $"Payload execution failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
