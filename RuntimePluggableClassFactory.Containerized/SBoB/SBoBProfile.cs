using System.Collections.Generic;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.SBoB
{
    /// <summary>
    /// Dialect of a canonical SBoB profile (SBOB-Forge TDS §10.4).
    /// The two dialects are isomorphic below the header; the canonical embedded
    /// form is the kubescape dialect, the registry dialect is produced by the
    /// guarded header-level rewrite (sbobforge convert).
    /// </summary>
    public enum SBoBDialect
    {
        /// <summary>spdx.softwarecomposition.kubescape.io/v1beta1 / ContainerProfile — the embedded nupkg form.</summary>
        KubescapeContainerProfile,

        /// <summary>sbob.k8sstormcenter.io/v1 / SoftwareBillOfBehavior — the Software-Bill-of-Behavior registry form.</summary>
        RegistrySoftwareBillOfBehavior
    }

    /// <summary>
    /// Observed exec(2) behavior from an SBoB profile.
    /// Args is never empty in canonical output: the emitter falls back to
    /// [path, "⋯⋯"] (U+22EF doubled) when the exact argv is not statically known.
    /// </summary>
    public sealed class SBoBExecBehavior
    {
        public string Path { get; }
        public IReadOnlyList<string> Args { get; }

        public SBoBExecBehavior(string path, IReadOnlyList<string> args)
        {
            Path = path;
            Args = args;
        }
    }

    /// <summary>Observed open(2) behavior from an SBoB profile.</summary>
    public sealed class SBoBOpenBehavior
    {
        public string Path { get; }
        public IReadOnlyList<string> Flags { get; }

        public SBoBOpenBehavior(string path, IReadOnlyList<string> flags)
        {
            Path = path;
            Flags = flags;
        }
    }

    /// <summary>A single network port entry from an ingress/egress behavior.</summary>
    public sealed class SBoBPortBehavior
    {
        public int Port { get; }
        public string Protocol { get; }

        public SBoBPortBehavior(int port, string protocol)
        {
            Port = port;
            Protocol = protocol;
        }
    }

    /// <summary>
    /// An ingress or egress behavior entry. Canonical SBOB-Forge output always
    /// emits type "external"; entries are bound by identifier and optionally
    /// carry dnsNames/ipAddresses and a ports list.
    /// </summary>
    public sealed class SBoBNetworkBehavior
    {
        public string Identifier { get; }
        public string Type { get; }
        public IReadOnlyList<string> DnsNames { get; }
        public IReadOnlyList<string> IpAddresses { get; }
        public IReadOnlyList<SBoBPortBehavior> Ports { get; }

        public SBoBNetworkBehavior(
            string identifier,
            string type,
            IReadOnlyList<string> dnsNames,
            IReadOnlyList<string> ipAddresses,
            IReadOnlyList<SBoBPortBehavior> ports)
        {
            Identifier = identifier;
            Type = type;
            DnsNames = dnsNames;
            IpAddresses = ipAddresses;
            Ports = ports;
        }
    }

    /// <summary>
    /// Parsed SBoB ContainerProfile in canonical emission form
    /// (SBOB-Forge TDS §8.1). The profile is data, never behavior.
    /// </summary>
    public sealed class SBoBProfile
    {
        public SBoBDialect Dialect { get; }
        public string ApiVersion { get; }
        public string Kind { get; }
        public string MetadataName { get; }
        public string MetadataNamespace { get; }
        public IReadOnlyDictionary<string, string> Annotations { get; }
        public IReadOnlyList<string> Architectures { get; }
        public string ImageId { get; }
        public IReadOnlyList<SBoBExecBehavior> Execs { get; }
        public IReadOnlyList<SBoBOpenBehavior> Opens { get; }
        public IReadOnlyList<SBoBNetworkBehavior> Ingress { get; }
        public IReadOnlyList<SBoBNetworkBehavior> Egress { get; }

        public SBoBProfile(
            SBoBDialect dialect,
            string apiVersion,
            string kind,
            string metadataName,
            string metadataNamespace,
            IReadOnlyDictionary<string, string> annotations,
            IReadOnlyList<string> architectures,
            string imageId,
            IReadOnlyList<SBoBExecBehavior> execs,
            IReadOnlyList<SBoBOpenBehavior> opens,
            IReadOnlyList<SBoBNetworkBehavior> ingress,
            IReadOnlyList<SBoBNetworkBehavior> egress)
        {
            Dialect = dialect;
            ApiVersion = apiVersion;
            Kind = kind;
            MetadataName = metadataName;
            MetadataNamespace = metadataNamespace;
            Annotations = annotations;
            Architectures = architectures;
            ImageId = imageId;
            Execs = execs;
            Opens = opens;
            Ingress = ingress;
            Egress = egress;
        }

        /// <summary>develapp.io/behavior-hash annotation (opaque audit value: sha256 over the normalized IR, not the YAML text — cannot be recomputed offline).</summary>
        public string? BehaviorHash => TryGetAnnotation(SBoBCanonicalParser.BehaviorHashAnnotation);

        /// <summary>develapp.io/generator annotation (e.g. sbobforge@0.1.0).</summary>
        public string? Generator => TryGetAnnotation(SBoBCanonicalParser.GeneratorAnnotation);

        /// <summary>develapp.io/grammar-lock-sha256 annotation.</summary>
        public string? GrammarLockHash => TryGetAnnotation(SBoBCanonicalParser.GrammarLockAnnotation);

        /// <summary>True when the profile declares no behavior at all (no execs, opens, ingress, egress sections).</summary>
        public bool IsEmpty => Execs.Count == 0 && Opens.Count == 0 && Ingress.Count == 0 && Egress.Count == 0;

        private string? TryGetAnnotation(string key) => Annotations.TryGetValue(key, out var value) ? value : null;
    }
}
