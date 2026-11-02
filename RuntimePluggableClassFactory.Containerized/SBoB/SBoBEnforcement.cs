using System;
using System.Collections.Generic;
using System.Linq;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.SBoB
{
    /// <summary>
    /// SBoB enforcement mode (the SBoB phase-3 state machine, org decision #39).
    /// Rollout is always per-package Monitor → Audit → Enforce; nothing goes straight to Enforce.
    /// </summary>
    public enum SBoBEnforcementMode
    {
        /// <summary>Verification skipped entirely (development only).</summary>
        Off = 0,

        /// <summary>Verify and record; violations are informational and never alter results.</summary>
        Monitor = 1,

        /// <summary>Verify, record, and surface violations as warnings on results; nothing is denied.</summary>
        Audit = 2,

        /// <summary>Verify and deny the load/deployment on any blocking violation (fail-closed).</summary>
        Enforce = 3
    }

    /// <summary>Severity of a single SBoB verification violation.</summary>
    public enum SBoBViolationSeverity
    {
        /// <summary>Recorded and surfaced, but never blocking on its own.</summary>
        Warning = 0,

        /// <summary>Blocking: denies the deployment in Enforce mode.</summary>
        Error = 1
    }

    /// <summary>A single SBoB verification finding.</summary>
    public sealed class SBoBViolation
    {
        public string Rule { get; }
        public SBoBViolationSeverity Severity { get; }
        public string Message { get; }

        public SBoBViolation(string rule, SBoBViolationSeverity severity, string message)
        {
            Rule = rule;
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"[{Rule}] {Message}";
    }

    /// <summary>
    /// Org-configurable SBoB policy (org decision #39): the behavior allowlist the runtime
    /// enforces on top of the profile's own declarations. Defaults are deny-flavored:
    /// a required canonical profile, mandatory purl binding, and forbidden shell exec paths.
    /// </summary>
    public sealed class SBoBPolicy
    {
        /// <summary>Exec paths a plugin profile must never declare (shells defeat every other rule).</summary>
        public static readonly IReadOnlyList<string> DefaultForbiddenExecPaths = new[]
        {
            "/bin/sh", "/bin/bash", "/bin/dash", "/bin/ash", "/bin/ksh",
            "/usr/bin/sh", "/usr/bin/bash", "/usr/bin/dash", "/usr/bin/ash", "/usr/bin/ksh"
        };

        /// <summary>True (default) when a package must carry an embedded canonical profile.</summary>
        public bool RequireProfile { get; init; } = true;

        /// <summary>True (default) when the profile must be purl-bound to the package it ships in (when the identity is known).</summary>
        public bool RequirePurlBinding { get; init; } = true;

        /// <summary>Exec paths the profile must not declare. Default: common shells.</summary>
        public IReadOnlyList<string> ForbiddenExecPaths { get; init; } = DefaultForbiddenExecPaths;

        /// <summary>Open path prefixes the profile must not declare (e.g. "/etc/", "/root/"). Default: none.</summary>
        public IReadOnlyList<string> ForbiddenOpenPathPrefixes { get; init; } = Array.Empty<string>();

        /// <summary>Open flags the profile must not declare (e.g. "O_RDWR"). Default: none.</summary>
        public IReadOnlyList<string> ForbiddenOpenFlags { get; init; } = Array.Empty<string>();

        /// <summary>False to deny every egress entry in Enforce mode. Default: true (egress is then bounded by the derived NetworkPolicy).</summary>
        public bool AllowExternalEgress { get; init; } = true;

        /// <summary>
        /// Egress DNS allowlist: null (default) = unrestricted; otherwise every egress entry must be covered by an
        /// exact entry or a "*.suffix" wildcard. Entries without DNS names cannot be checked and violate.
        /// </summary>
        public IReadOnlyList<string>? AllowedEgressDnsNames { get; init; }

        /// <summary>False to deny every ingress entry in Enforce mode. Default: true (bounded by AllowedIngressPorts when set).</summary>
        public bool AllowExternalIngress { get; init; } = true;

        /// <summary>Ingress port allowlist: null (default) = unrestricted; otherwise every declared ingress port must be listed.</summary>
        public IReadOnlyList<int>? AllowedIngressPorts { get; init; }

        /// <summary>True (default) to warn when a profile declares no behavior at all — suspicious for a plugin package.</summary>
        public bool WarnOnEmptyProfile { get; init; } = true;
    }

    /// <summary>Outcome of verifying a package against its embedded SBoB profile. Mode-independent; the caller decides what to do with it.</summary>
    public sealed class SBoBVerificationResult
    {
        /// <summary>True when exactly one profile for the target os was found and parsed.</summary>
        public bool ProfileFound { get; init; }

        /// <summary>The parsed profile (null when not found or unparsable).</summary>
        public SBoBProfile? Profile { get; init; }

        /// <summary>Package-internal path of the verified profile (e.g. sbob/example-core.linux.sbob.yaml).</summary>
        public string? ProfilePath { get; init; }

        /// <summary>The package identity the binding was checked against, when provided.</summary>
        public string? ExpectedPurl { get; init; }

        /// <summary>All findings, structural and policy, warnings and errors.</summary>
        public IReadOnlyList<SBoBViolation> Violations { get; init; } = Array.Empty<SBoBViolation>();

        /// <summary>True when any Error-severity violation exists.</summary>
        public bool HasErrors
        {
            get
            {
                foreach (var violation in Violations)
                {
                    if (violation.Severity == SBoBViolationSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>False when the deployment must be denied in Enforce mode: any Error-severity violation (missing required profile, non-canonical profile, binding mismatch, policy violation).</summary>
        public bool Allowed => !HasErrors;

        /// <summary>Error-severity violation messages, formatted for result.Errors.</summary>
        public IEnumerable<string> ErrorMessages
        {
            get
            {
                foreach (var violation in Violations)
                {
                    if (violation.Severity == SBoBViolationSeverity.Error)
                    {
                        yield return violation.ToString();
                    }
                }
            }
        }

        /// <summary>All violation messages (warnings first, then errors), formatted for result.Warnings.</summary>
        public IEnumerable<string> WarningMessages
        {
            get
            {
                foreach (var violation in Violations)
                {
                    if (violation.Severity == SBoBViolationSeverity.Warning)
                    {
                        yield return violation.ToString();
                    }
                }
            }
        }
    }

    /// <summary>
    /// SBoB policy evaluation: turns a parsed profile (or its absence) plus the caller's
    /// policy into a violation list. Pure; no I/O.
    /// </summary>
    public static class SBoBPolicyEvaluator
    {
        /// <summary>Evaluate the policy against a parsed profile (null when none was found).</summary>
        public static IReadOnlyList<SBoBViolation> Evaluate(SBoBProfile? profile, string? expectedPurl, SBoBPolicy policy)
        {
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var violations = new List<SBoBViolation>();
            if (profile is null)
            {
                if (policy.RequireProfile)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBProfileRequired", SBoBViolationSeverity.Error,
                        "no canonical SBoB profile was found in the package (expected sbob/<package>.<os>.sbob.yaml; org decision #39)"));
                }
                return violations;
            }

            if (policy.RequirePurlBinding)
            {
                if (expectedPurl is null)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBPurlBindingUnchecked", SBoBViolationSeverity.Warning,
                        "package identity was not provided; the profile's purl binding could not be checked"));
                }
                else if (!string.Equals(expectedPurl, profile.ImageId, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new SBoBViolation(
                        "SBoBPurlBinding", SBoBViolationSeverity.Error,
                        $"profile is bound to '{profile.ImageId}' but the package is '{expectedPurl}' (a profile that does not match its package is rejected regardless of mode)"));
                }
            }

            if (policy.WarnOnEmptyProfile && profile.IsEmpty)
            {
                violations.Add(new SBoBViolation(
                    "SBoBEmptyProfile", SBoBViolationSeverity.Warning,
                    "profile declares no behavior at all (no execs, opens, ingress, egress) — suspicious for a plugin package"));
            }

            foreach (var exec in profile.Execs)
            {
                foreach (var forbidden in policy.ForbiddenExecPaths)
                {
                    if (string.Equals(exec.Path, forbidden, StringComparison.Ordinal))
                    {
                        violations.Add(new SBoBViolation(
                            "SBoBForbiddenExec", SBoBViolationSeverity.Error,
                            $"profile declares execution of forbidden path '{exec.Path}'"));
                    }
                }
            }

            foreach (var open in profile.Opens)
            {
                foreach (var prefix in policy.ForbiddenOpenPathPrefixes)
                {
                    if (open.Path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        violations.Add(new SBoBViolation(
                            "SBoBForbiddenOpen", SBoBViolationSeverity.Error,
                            $"profile declares opening forbidden path '{open.Path}' (prefix '{prefix}')"));
                    }
                }
                foreach (var flag in policy.ForbiddenOpenFlags)
                {
                    foreach (var declaredFlag in open.Flags)
                    {
                        if (string.Equals(declaredFlag, flag, StringComparison.Ordinal))
                        {
                            violations.Add(new SBoBViolation(
                                "SBoBForbiddenOpenFlag", SBoBViolationSeverity.Error,
                                $"profile declares opening '{open.Path}' with forbidden flag '{flag}'"));
                        }
                    }
                }
            }

            foreach (var egress in profile.Egress)
            {
                if (!policy.AllowExternalEgress)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBExternalEgress", SBoBViolationSeverity.Error,
                        $"profile declares external egress '{egress.Identifier}' but the policy denies external egress"));
                    continue;
                }

                if (policy.AllowedEgressDnsNames is { } allowList)
                {
                    if (egress.DnsNames.Count == 0)
                    {
                        violations.Add(new SBoBViolation(
                            "SBoBEgressDns", SBoBViolationSeverity.Error,
                            $"profile declares external egress '{egress.Identifier}' without DNS names — it cannot be checked against the egress DNS allowlist"));
                    }
                    else if (!IsCoveredByDnsAllowList(egress.DnsNames, allowList))
                    {
                        violations.Add(new SBoBViolation(
                            "SBoBEgressDns", SBoBViolationSeverity.Error,
                            $"profile declares external egress to '{string.Join(", ", egress.DnsNames)}' which is not on the egress DNS allowlist"));
                    }
                }
            }

            foreach (var ingress in profile.Ingress)
            {
                if (!policy.AllowExternalIngress)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBExternalIngress", SBoBViolationSeverity.Error,
                        $"profile declares external ingress '{ingress.Identifier}' but the policy denies external ingress"));
                    continue;
                }

                if (policy.AllowedIngressPorts is { } ports)
                {
                    foreach (var port in ingress.Ports)
                    {
                        if (!ports.Contains(port.Port))
                        {
                            violations.Add(new SBoBViolation(
                                "SBoBIngressPort", SBoBViolationSeverity.Error,
                                $"profile declares ingress port {port.Port}/{port.Protocol} which is not on the ingress port allowlist"));
                        }
                    }
                }
            }

            return violations;
        }

        /// <summary>
        /// Derive the CRPCF NetworkPolicy from the profile's egress declarations.
        /// Canonical profiles mark all network entries external (SBOB-Forge v1), so the mapping is:
        /// no egress → Isolated; any egress → Restricted.
        /// </summary>
        public static NetworkPolicy DeriveNetworkPolicy(SBoBProfile profile)
        {
            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            return profile.Egress.Count == 0 ? NetworkPolicy.Isolated : NetworkPolicy.Restricted;
        }

        private static bool IsCoveredByDnsAllowList(IReadOnlyList<string> dnsNames, IReadOnlyList<string> allowList)
        {
            foreach (var dnsName in dnsNames)
            {
                foreach (var allowed in allowList)
                {
                    if (string.Equals(dnsName, allowed, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (allowed.StartsWith("*.", StringComparison.Ordinal)
                        && dnsName.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase)
                        && dnsName.Length > allowed.Length - 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Helpers for the package purl binding (pkg:nuget/&lt;lowercased-id&gt;@&lt;version&gt;, SBOB-Forge TDS §4).</summary>
    public static class SBoBPackagePurl
    {
        /// <summary>Build the expected purl for a NuGet package identity (id lowercased, like the SBOB-Forge emitter).</summary>
        public static string ForNuGet(string packageId, string version)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("package id must not be empty", nameof(packageId));
            }
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("package version must not be empty", nameof(version));
            }
            return $"pkg:nuget/{packageId.ToLowerInvariant()}@{version}";
        }
    }
}
