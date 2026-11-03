using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Containerized.SBoB;
using DevelApp.Utility.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Containerized.Tests
{
    /// <summary>
    /// Hermetic tests for SBoB verification and enforcement (org decision #39):
    /// the strict canonical parser, the package verifier over in-memory package zips,
    /// the policy evaluator, and the enforcing orchestrator decorator. No network,
    /// no containers, no real NuGet packages.
    /// </summary>
    [TestClass]
    public class SBoBVerificationTests
    {
        private const string CanonicalProfile = CanonicalProfileBody + "\n";

        private const string CanonicalProfileBody = """
            apiVersion: spdx.softwarecomposition.kubescape.io/v1beta1
            kind: ContainerProfile
            metadata:
              name: example-core.linux
              namespace: develapp
              annotations:
                kubescape.io/managed-by: User
                develapp.io/binding: package-purl
                develapp.io/generator: sbobforge@0.1.0
                develapp.io/grammar-lock-sha256: 8e5651bf16ecf80ab37c143263072547922c294ad593ca17fe68cdef116ffe57
                develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe
            spec:
              architectures: [amd64]
              imageID: pkg:nuget/example.core@1.0.0
              execs:
                - path: /usr/bin/git
                  args: [/usr/bin/git, status]
              opens:
                - path: /etc/app/config.json
                  flags: [O_RDONLY]
                - path: /var/log/app.log
                  flags: [O_WRONLY, O_CREAT, O_TRUNC]
              ingress:
                - identifier: port-8080-ingress
                  type: external
                  ports:
                    - { port: 8080, protocol: TCP }
              egress:
                - identifier: api.example.com-egress
                  type: external
                  dnsNames: [api.example.com]
                  ports:
                    - { port: 443, protocol: TCP }
            """;

        private const string ShellExecProfile = """
            apiVersion: spdx.softwarecomposition.kubescape.io/v1beta1
            kind: ContainerProfile
            metadata:
              name: example-core.linux
              namespace: develapp
              annotations:
                kubescape.io/managed-by: User
                develapp.io/binding: package-purl
                develapp.io/generator: sbobforge@0.1.0
                develapp.io/grammar-lock-sha256: 8e5651bf16ecf80ab37c143263072547922c294ad593ca17fe68cdef116ffe57
                develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe
            spec:
              imageID: pkg:nuget/example.core@1.0.0
              execs:
                - path: /bin/sh
                  args: [/bin/sh, "-c"]
            """;

        private const string NoEgressProfile = """
            apiVersion: spdx.softwarecomposition.kubescape.io/v1beta1
            kind: ContainerProfile
            metadata:
              name: example-core.linux
              namespace: develapp
              annotations:
                kubescape.io/managed-by: User
                develapp.io/binding: package-purl
                develapp.io/generator: sbobforge@0.1.0
                develapp.io/grammar-lock-sha256: 8e5651bf16ecf80ab37c143263072547922c294ad593ca17fe68cdef116ffe57
                develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe
            spec:
              imageID: pkg:nuget/example.core@1.0.0
              execs:
                - path: /usr/bin/dotnet
                  args: [/usr/bin/dotnet, run]
            """;

        private const string EmptyBehaviorProfile = """
            apiVersion: spdx.softwarecomposition.kubescape.io/v1beta1
            kind: ContainerProfile
            metadata:
              name: example-core.linux
              namespace: develapp
              annotations:
                kubescape.io/managed-by: User
                develapp.io/binding: package-purl
                develapp.io/generator: sbobforge@0.1.0
                develapp.io/grammar-lock-sha256: 8e5651bf16ecf80ab37c143263072547922c294ad593ca17fe68cdef116ffe57
                develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe
            spec:
              imageID: pkg:nuget/example.core@1.0.0
            """;

        private const string RegistryDialectProfile = """
            apiVersion: sbob.k8sstormcenter.io/v1
            kind: SoftwareBillOfBehavior
            metadata:
              name: example-core.linux
              namespace: develapp
              annotations:
                sbob.spec-version: "0.0.4"
                develapp.io/binding: package-purl
                develapp.io/generator: sbobforge@0.1.0
                develapp.io/grammar-lock-sha256: 8e5651bf16ecf80ab37c143263072547922c294ad593ca17fe68cdef116ffe57
                develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe
            spec:
              imageID: pkg:nuget/example.core@1.0.0
              execs:
                - path: /usr/bin/git
                  args: [/usr/bin/git, status]
            """;

        // ---------------------------------------------------------------------
        // Parser
        // ---------------------------------------------------------------------

        [TestMethod]
        public void ParseCanonicalKubescapeProfile_ParsesAllSections()
        {
            var profile = SBoBCanonicalParser.Parse(CanonicalProfile);

            Assert.AreEqual(SBoBDialect.KubescapeContainerProfile, profile.Dialect);
            Assert.AreEqual("example-core.linux", profile.MetadataName);
            Assert.AreEqual("develapp", profile.MetadataNamespace);
            Assert.AreEqual("pkg:nuget/example.core@1.0.0", profile.ImageId);
            Assert.AreEqual(5, profile.Annotations.Count);
            Assert.AreEqual("sbobforge@0.1.0", profile.Generator);
            Assert.AreEqual("4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe", profile.BehaviorHash);
            CollectionAssert.AreEqual(new[] { "amd64" }, profile.Architectures.ToArray());
            Assert.AreEqual(1, profile.Execs.Count);
            Assert.AreEqual("/usr/bin/git", profile.Execs[0].Path);
            CollectionAssert.AreEqual(new[] { "/usr/bin/git", "status" }, profile.Execs[0].Args.ToArray());
            Assert.AreEqual(2, profile.Opens.Count);
            CollectionAssert.AreEqual(new[] { "O_WRONLY", "O_CREAT", "O_TRUNC" }, profile.Opens[1].Flags.ToArray());
            Assert.AreEqual(1, profile.Ingress.Count);
            Assert.AreEqual(8080, profile.Ingress[0].Ports[0].Port);
            Assert.AreEqual("TCP", profile.Ingress[0].Ports[0].Protocol);
            Assert.AreEqual(1, profile.Egress.Count);
            CollectionAssert.AreEqual(new[] { "api.example.com" }, profile.Egress[0].DnsNames.ToArray());
            Assert.AreEqual(443, profile.Egress[0].Ports[0].Port);
            Assert.IsFalse(profile.IsEmpty);
        }

        [TestMethod]
        public void ParseRegistryDialect_SpecVersionAnnotationAccepted()
        {
            var profile = SBoBCanonicalParser.Parse(RegistryDialectProfile + "\n");

            Assert.AreEqual(SBoBDialect.RegistrySoftwareBillOfBehavior, profile.Dialect);
            Assert.AreEqual(SBoBCanonicalParser.RegistryApiVersion, profile.ApiVersion);
            Assert.AreEqual(SBoBCanonicalParser.RegistryKind, profile.Kind);
            Assert.AreEqual("0.0.4", profile.Annotations[SBoBCanonicalParser.SpecVersionAnnotation]);
            Assert.IsFalse(profile.Annotations.ContainsKey(SBoBCanonicalParser.ManagedByAnnotation));
            Assert.AreEqual(1, profile.Execs.Count);
        }

        [TestMethod]
        public void ParseQuotedWildcardPath_IsAccepted()
        {
            // Canonical quoting: values containing U+22EF are double-quoted (TDS §8.1).
            var text = CanonicalProfile.Replace(
                "    - path: /etc/app/config.json\n      flags: [O_RDONLY]",
                "    - path: \"/app/\u22EF/cache/last.json\"\n      flags: [O_WRONLY, O_CREAT, O_TRUNC]");

            var profile = SBoBCanonicalParser.Parse(text);

            Assert.AreEqual("/app/\u22EF/cache/last.json", profile.Opens[0].Path);
        }

        [TestMethod]
        public void ParseUnquotedWildcardCharacter_IsRejected()
        {
            // The canonical writer quotes any value containing U+22EF; a plain one is non-canonical.
            var text = CanonicalProfile.Replace(
                "    - path: /etc/app/config.json",
                "    - path: /app/\u22EF/cache/last.json");

            AssertCanonicalFormatRejected(text);
        }

        [TestMethod]
        public void ParseAsciiEllipsis_IsRejected()
        {
            var text = CanonicalProfile.Replace("args: [/usr/bin/git, status]", "args: [/usr/bin/git, ...]");

            AssertCanonicalFormatRejected(text);
        }

        [TestMethod]
        public void ParseCrLfLineEndings_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("\n", "\r\n"));
        }

        [TestMethod]
        public void ParseTab_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("  annotations:", "\tannotations:"));
        }

        [TestMethod]
        public void ParseTrailingWhitespace_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("  execs:", "  execs: "));
        }

        [TestMethod]
        public void ParseDocumentMarker_IsRejected()
        {
            AssertCanonicalFormatRejected("---\n" + CanonicalProfile);
            AssertCanonicalFormatRejected(CanonicalProfile + "...\n");
        }

        [TestMethod]
        public void ParseComment_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("spec:", "spec: # trailing comment is never canonical"));
        }

        [TestMethod]
        public void ParseMissingBehaviorHashAnnotation_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "    develapp.io/behavior-hash: 4b9aef2051a80e93fac0f7d13aad127bc9821cb3d456cf4c86b7c4c09de6fafe\n", ""));
        }

        [TestMethod]
        public void ParseExtraAnnotation_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "spec:",
                "    develapp.io/unknown: value\nspec:"));
        }

        [TestMethod]
        public void ParseUnknownSpecSection_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "  execs:",
                "  capabilities:\n    - capability: CAP_SYS_ADMIN\n  execs:"));
        }

        [TestMethod]
        public void ParseOutOfOrderSections_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "  ingress:\n    - identifier: port-8080-ingress\n      type: external\n      ports:\n        - { port: 8080, protocol: TCP }\n  egress:",
                "  egress:\n    - identifier: api.example.com-egress\n      type: external\n      dnsNames: [api.example.com]\n      ports:\n        - { port: 443, protocol: TCP }\n  ingress:\n    - identifier: port-8080-ingress\n      type: external\n      ports:\n        - { port: 8080, protocol: TCP }"));
        }

        [TestMethod]
        public void ParseOutOfOrderFields_IsRejected()
        {
            // Canonical field order in execs entries is path then args.
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "    - path: /usr/bin/git\n      args: [/usr/bin/git, status]",
                "    - args: [/usr/bin/git, status]\n      path: /usr/bin/git"));
        }

        [TestMethod]
        public void ParseInternalNetType_IsRejected()
        {
            // Canonical v1 output has no internal type; hand-authored registry samples are not canonical.
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("      type: external\n      dnsNames:", "      type: internal\n      dnsNames:"));
        }

        [TestMethod]
        public void ParseSingleQuotedScalar_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace(
                "    - path: /etc/app/config.json",
                "    - path: '/etc/app/config.json'"));
        }

        [TestMethod]
        public void ParseLeadingSpaceInValue_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("  name: example-core.linux", "  name:  example-core.linux"));
        }

        [TestMethod]
        public void ParseEmptyInput_IsRejected()
        {
            Assert.ThrowsException<SBoBCanonicalFormatException>(() => SBoBCanonicalParser.Parse(""));
            Assert.ThrowsException<SBoBCanonicalFormatException>(() => SBoBCanonicalParser.Parse("\n"));
        }

        [TestMethod]
        public void ParseMissingTrailingNewline_IsRejected()
        {
            Assert.ThrowsException<SBoBCanonicalFormatException>(() => SBoBCanonicalParser.Parse(CanonicalProfile.TrimEnd('\n')));
        }

        [TestMethod]
        public void ParseMalformedPurl_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("imageID: pkg:nuget/example.core@1.0.0", "imageID: example.core.1.0.0"));
        }

        [TestMethod]
        public void ParseSingleQuotedDialectKindMismatch_IsRejected()
        {
            AssertCanonicalFormatRejected(CanonicalProfile.Replace("kind: ContainerProfile", "kind: SoftwareBillOfBehavior"));
        }

        // ---------------------------------------------------------------------
        // Verifier
        // ---------------------------------------------------------------------

        [TestMethod]
        public async Task VerifyCanonicalProfile_IsAllowed()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                ExpectedPurl = "pkg:nuget/example.core@1.0.0"
            });

            Assert.IsTrue(result.ProfileFound, string.Join("; ", result.Violations.Select(v => v.ToString())));
            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(0, result.Violations.Count);
            Assert.AreEqual("sbob/example-core.linux.sbob.yaml", result.ProfilePath);
            Assert.IsNotNull(result.Profile);
            Assert.AreEqual("pkg:nuget/example.core@1.0.0", result.Profile.ImageId);
        }

        [TestMethod]
        public async Task VerifyMissingProfile_Required_IsDenied()
        {
            using var package = CreatePackage(("tools/plugin.dll", "binary"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.ProfileFound);
            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBProfileRequired", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyMissingProfile_NotRequired_IsAllowedWithWarning()
        {
            using var package = CreatePackage(("tools/plugin.dll", "binary"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { RequireProfile = false }
            });

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(0, result.Violations.Count);
        }

        [TestMethod]
        public async Task VerifyPurlBinding_Mismatch_IsDenied()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                ExpectedPurl = "pkg:nuget/other.package@2.0.0"
            });

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBPurlBinding", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyPurlBinding_WithoutIdentity_Warns()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsTrue(result.Allowed);
            AssertHasRule(result, "SBoBPurlBindingUnchecked", SBoBViolationSeverity.Warning);
        }

        [TestMethod]
        public async Task VerifyWrongTargetOs_HintInMessage()
        {
            using var package = CreatePackage(("sbob/example-core.windows.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.ProfileFound);
            AssertHasRule(result, "SBoBProfileRequired", SBoBViolationSeverity.Error);
            Assert.IsTrue(result.ErrorMessages.Any(m => m.Contains("other target os")),
                "the error should hint that profiles exist for other target os: " + string.Join("; ", result.ErrorMessages));
        }

        [TestMethod]
        public async Task VerifyMultipleProfilesForTargetOs_IsDenied()
        {
            using var package = CreatePackage(
                ("sbob/example-core.linux.sbob.yaml", CanonicalProfile),
                ("sbob/other.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBMultipleProfiles", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyNonCanonicalProfile_IsDenied()
        {
            var mangled = CanonicalProfile.Replace("\n", "\r\n");
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", mangled));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.ProfileFound);
            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBCanonicalForm", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyFileNameDoesNotMatchMetadata_IsDenied()
        {
            using var package = CreatePackage(("sbob/wrong-name.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBFileNameBinding", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyCorruptPackageStream_IsDenied()
        {
            using var garbage = new MemoryStream(Encoding.ASCII.GetBytes("this is not a zip archive"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(garbage);

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBPackageUnreadable", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyProvenanceSidecar_IsIgnored()
        {
            using var package = CreatePackage(
                ("sbob/example-core.linux.sbob.yaml", CanonicalProfile),
                ("sbob/example-core.linux.sbob.provenance.jsonl", "{\"testId\":\"a\"}\n"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                ExpectedPurl = "pkg:nuget/example.core@1.0.0"
            });

            Assert.IsTrue(result.Allowed);
        }

        [TestMethod]
        public async Task VerifyForbiddenShellExec_IsDenied()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", ShellExecProfile + "\n"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBForbiddenExec", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyExternalEgressNotAllowed_IsDenied()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { AllowExternalEgress = false }
            });

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBExternalEgress", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyEgressDnsAllowlist_ExactAndWildcard()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var denied = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { AllowedEgressDnsNames = new[] { "*.internal.develapp.io" } }
            });
            Assert.IsFalse(denied.Allowed);
            AssertHasRule(denied, "SBoBEgressDns", SBoBViolationSeverity.Error);

            var wildcardAllowed = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { AllowedEgressDnsNames = new[] { "*.example.com" } }
            });
            Assert.IsTrue(wildcardAllowed.Allowed);

            var exactAllowed = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { AllowedEgressDnsNames = new[] { "api.example.com" } }
            });
            Assert.IsTrue(exactAllowed.Allowed);
        }

        [TestMethod]
        public async Task VerifyIngressPortAllowlist_IsDenied()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { AllowedIngressPorts = new[] { 443 } }
            });

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBIngressPort", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyOpenPathPrefix_IsDenied()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package, new SBoBVerificationOptions
            {
                Policy = new SBoBPolicy { ForbiddenOpenPathPrefixes = new[] { "/etc/" } }
            });

            Assert.IsFalse(result.Allowed);
            AssertHasRule(result, "SBoBForbiddenOpen", SBoBViolationSeverity.Error);
        }

        [TestMethod]
        public async Task VerifyEmptyBehaviorProfile_WarnsButIsAllowed()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", EmptyBehaviorProfile + "\n"));
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsTrue(result.Allowed);
            AssertHasRule(result, "SBoBEmptyProfile", SBoBViolationSeverity.Warning);
        }

        [TestMethod]
        public async Task VerifyPreservesSeekableStreamPosition()
        {
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            package.Position = 3;
            var verifier = new SBoBPackageVerifier();

            var result = await verifier.VerifyAsync(package);

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(3, package.Position);
        }

        // ---------------------------------------------------------------------
        // Policy helpers
        // ---------------------------------------------------------------------

        [TestMethod]
        public void PurlForNuGet_LowercasesPackageId()
        {
            Assert.AreEqual("pkg:nuget/develapp.example.core@1.2.3", SBoBPackagePurl.ForNuGet("DevelApp.Example.Core", "1.2.3"));
        }

        [TestMethod]
        public void DeriveNetworkPolicy_NoEgressIsIsolated_AnyEgressIsRestricted()
        {
            var noEgress = SBoBCanonicalParser.Parse(NoEgressProfile + "\n");
            var withEgress = SBoBCanonicalParser.Parse(CanonicalProfile);

            Assert.AreEqual(NetworkPolicy.Isolated, SBoBPolicyEvaluator.DeriveNetworkPolicy(noEgress));
            Assert.AreEqual(NetworkPolicy.Restricted, SBoBPolicyEvaluator.DeriveNetworkPolicy(withEgress));
        }

        // ---------------------------------------------------------------------
        // Enforcing orchestrator
        // ---------------------------------------------------------------------

        [TestMethod]
        public async Task Enforce_CleanProfile_DeploysAndTightensNetworkPolicy()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce, ExpectedPurl = "pkg:nuget/example.core@1.0.0" });
            var request = CreateRequest(package, networkPolicy: NetworkPolicy.Unrestricted);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
            Assert.AreEqual(1, inner.DeployCount);
            Assert.AreEqual(NetworkPolicy.Restricted, inner.LastRequest!.Configuration.NetworkPolicy);
            // The request itself is tightened (the inner orchestrator sees the same object).
            Assert.AreEqual(NetworkPolicy.Restricted, request.Configuration.NetworkPolicy);
        }

        [TestMethod]
        public async Task Enforce_NoEgressProfile_TightensToIsolated()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", NoEgressProfile + "\n"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce, ExpectedPurl = "pkg:nuget/example.core@1.0.0" });
            var request = CreateRequest(package, networkPolicy: NetworkPolicy.Restricted);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(NetworkPolicy.Isolated, request.Configuration.NetworkPolicy);
        }

        [TestMethod]
        public async Task Enforce_Violation_DeniesWithoutCallingInner()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", ShellExecProfile + "\n"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce });
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, inner.DeployCount);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("SBoBForbiddenExec")),
                "expected the forbidden-exec violation in errors: " + string.Join("; ", result.Errors));
        }

        [TestMethod]
        public async Task Enforce_MissingProfile_IsDenied()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("tools/plugin.dll", "binary"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce });
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, inner.DeployCount);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("SBoBProfileRequired")));
        }

        [TestMethod]
        public async Task Enforce_CorruptPackageStream_IsDenied()
        {
            var inner = new RecordingOrchestrator();
            using var garbage = new MemoryStream(Encoding.ASCII.GetBytes("not a package"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce });
            var request = CreateRequest(garbage);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, inner.DeployCount);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("SBoBPackageUnreadable") || e.Contains("SBoBVerificationFailed")));
        }

        [TestMethod]
        public async Task Enforce_MetadataPurlMismatch_IsDenied()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce });
            var request = CreateRequest(package, metadata: new Dictionary<string, object>
            {
                ["PackageId"] = "Other.Package",
                ["PackageVersion"] = "9.9.9"
            });

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, inner.DeployCount);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("SBoBPurlBinding")));
        }

        [TestMethod]
        public async Task Enforce_MetadataPurlMatch_Deploys()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", CanonicalProfile));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Enforce });
            var request = CreateRequest(package, metadata: new Dictionary<string, object>
            {
                ["PackageId"] = "Example.Core",
                ["PackageVersion"] = "1.0.0"
            });

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
            Assert.AreEqual(1, inner.DeployCount);
        }

        [TestMethod]
        public async Task Audit_Violation_DeploysWithWarnings()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", ShellExecProfile + "\n"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Audit });
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, inner.DeployCount);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("SBoBForbiddenExec")),
                "Audit must surface violations as warnings: " + string.Join("; ", result.Warnings));
        }

        [TestMethod]
        public async Task Monitor_Violation_DeploysWithoutAlteringResult()
        {
            var inner = new RecordingOrchestrator();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", ShellExecProfile + "\n"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, new SBoBPackageVerifier(),
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Monitor });
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, inner.DeployCount);
            Assert.AreEqual(0, result.Warnings.Count());
        }

        [TestMethod]
        public async Task Off_SkipsVerificationAndDeploys()
        {
            var inner = new RecordingOrchestrator();
            var verifier = new CountingVerifier();
            using var package = CreatePackage(("sbob/example-core.linux.sbob.yaml", ShellExecProfile + "\n"));
            var orchestrator = new SBoBEnforcingOrchestrator(inner, verifier,
                new SBoBEnforcingOrchestratorOptions { Mode = SBoBEnforcementMode.Off });
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, inner.DeployCount);
            Assert.AreEqual(0, verifier.CallCount, "Off mode must not invoke the verifier");
        }

        [TestMethod]
        public async Task DefaultOptions_AreOff_SoPublicConsumersAreUnaffected()
        {
            // Decision #39 acceptance: SBoB profiles are optional for consumers of the public
            // nuget.org packages — default options must skip verification entirely.
            var inner = new RecordingOrchestrator();
            var verifier = new CountingVerifier();
            using var package = CreatePackage(("tools/plugin.dll", "no SBoB profile at all"));
            var defaultOptions = new SBoBEnforcingOrchestratorOptions();
            var orchestrator = new SBoBEnforcingOrchestrator(inner, verifier, defaultOptions);
            var request = CreateRequest(package);

            var result = await orchestrator.DeployPluginAsync(request);

            Assert.AreEqual(SBoBEnforcementMode.Off, defaultOptions.Mode,
                "the default enforcement mode must be Off (opt-in)");
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, inner.DeployCount);
            Assert.AreEqual(0, verifier.CallCount, "default options must not invoke the verifier");
        }

        [TestMethod]
        public void Constructor_RejectsNulls()
        {
            var inner = new RecordingOrchestrator();
            var verifier = new SBoBPackageVerifier();

            Assert.ThrowsException<ArgumentNullException>(() => new SBoBEnforcingOrchestrator(null!, verifier));
            Assert.ThrowsException<ArgumentNullException>(() => new SBoBEnforcingOrchestrator(inner, null!));
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static void AssertCanonicalFormatRejected(string text) =>
            Assert.ThrowsException<SBoBCanonicalFormatException>(() => SBoBCanonicalParser.Parse(text),
                "expected the parser to reject non-canonical input");

        private static void AssertHasRule(SBoBVerificationResult result, string rule, SBoBViolationSeverity severity)
        {
            var violation = result.Violations.FirstOrDefault(v => v.Rule == rule);
            Assert.IsNotNull(violation, $"expected a '{rule}' violation; got: {string.Join("; ", result.Violations.Select(v => v.ToString()))}");
            Assert.AreEqual(severity, violation.Severity);
        }

        private static MemoryStream CreatePackage(params (string Path, string Content)[] files)
        {
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in files)
                {
                    var entry = zip.CreateEntry(path);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }
            }
            stream.Position = 0;
            return stream;
        }

        private static PluginDeploymentRequest CreateRequest(
            Stream packageStream,
            Dictionary<string, object>? metadata = null,
            NetworkPolicy networkPolicy = NetworkPolicy.Restricted)
            => new()
            {
                PackageStream = packageStream,
                TargetPlatform = "linux",
                Configuration = new DeploymentConfiguration { NetworkPolicy = networkPolicy },
                Metadata = metadata ?? new Dictionary<string, object>()
            };

        private sealed class RecordingOrchestrator : IContainerizedPluginOrchestrator
        {
            public int DeployCount { get; private set; }
            public PluginDeploymentRequest? LastRequest { get; private set; }

#pragma warning disable CS0067 // events are not raised by this recording fake
            public event EventHandler<PluginEventArgs>? PluginDeployed;
            public event EventHandler<PluginEventArgs>? PluginExecuted;
            public event EventHandler<PluginEventArgs>? PluginFailed;
            public event EventHandler<PluginEventArgs>? PluginUndeployed;
#pragma warning restore CS0067

            public Task<PluginDeploymentResult> DeployPluginAsync(PluginDeploymentRequest request, CancellationToken cancellationToken = default)
            {
                DeployCount++;
                LastRequest = request;
                return Task.FromResult(new PluginDeploymentResult { Success = true, DeploymentId = "deploy-1" });
            }

            public Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(ContainerizedPluginExecutionRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult(new ContainerizedPluginExecutionResult { Success = true });

            public Task<ContainerizedPluginInfo?> GetPluginInfoAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
                => Task.FromResult<ContainerizedPluginInfo?>(null);

            public Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(PluginListOptions? options = null, CancellationToken cancellationToken = default)
                => Task.FromResult<IEnumerable<ContainerizedPluginInfo>>(Array.Empty<ContainerizedPluginInfo>());

            public Task UndeployPluginAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<PluginHealth> GetPluginHealthAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
                => Task.FromResult(new PluginHealth { IsHealthy = true });

            public Task NotifyPluginUpdateAsync(PluginIdentifier pluginId, SemanticVersionNumber newVersion, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }

        private sealed class CountingVerifier : ISBoBPackageVerifier
        {
            public int CallCount { get; private set; }

            public Task<SBoBVerificationResult> VerifyAsync(Stream packageStream, SBoBVerificationOptions? options = null, CancellationToken cancellationToken = default)
            {
                CallCount++;
                return Task.FromResult(new SBoBVerificationResult());
            }
        }
    }
}
