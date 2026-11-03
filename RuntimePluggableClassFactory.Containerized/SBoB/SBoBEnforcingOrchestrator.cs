using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.Utility.Model;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.SBoB
{
    /// <summary>Options for the SBoB enforcing orchestrator (org decision #39).</summary>
    public sealed class SBoBEnforcingOrchestratorOptions
    {
        /// <summary>
        /// Enforcement mode. Default: Off — SBoB verification is opt-in (decision #39 acceptance:
        /// SBoB profiles are optional for consumers of the public nuget.org packages, which never
        /// ship one). DevelApp-internal orchestrators opt in explicitly and still roll out per
        /// package Monitor → Audit → Enforce; nothing goes straight to Enforce.
        /// </summary>
        public SBoBEnforcementMode Mode { get; init; } = SBoBEnforcementMode.Off;

        /// <summary>The policy evaluated against every embedded profile.</summary>
        public SBoBPolicy Policy { get; init; } = new();

        /// <summary>
        /// Target os used to select the embedded profile. When null (default), the request's
        /// TargetPlatform is used (lowercased); an empty TargetPlatform falls back to "linux"
        /// (the SBOB-Forge default, org decision #9).
        /// </summary>
        public string? TargetOs { get; init; }

        /// <summary>
        /// Expected package purl. When null (default), it is derived per request from the
        /// deployment request's Metadata keys "PackageId"/"PackageVersion"; when neither is
        /// present the binding check is skipped with a recorded warning.
        /// </summary>
        public string? ExpectedPurl { get; init; }
    }

    /// <summary>
    /// SBoB-enforcing decorator around any <see cref="IContainerizedPluginOrchestrator"/> (org decision #39).
    /// In Enforce mode the deployment is denied — without calling the inner orchestrator — when the
    /// package's embedded SBoB profile is missing, not canonical, not purl-bound, or violates the policy.
    /// In Enforce mode the request's NetworkPolicy is additionally tightened to the profile's derived
    /// policy (no egress → Isolated, any egress → Restricted) — tightening only, never loosening.
    /// Audit appends violations as warnings; Monitor records without altering results; Off skips
    /// verification. The SBoB is data: this gate never executes profile content.
    /// </summary>
    public sealed class SBoBEnforcingOrchestrator : IContainerizedPluginOrchestrator
    {
        private readonly IContainerizedPluginOrchestrator _inner;
        private readonly ISBoBPackageVerifier _verifier;
        private readonly SBoBEnforcingOrchestratorOptions _options;

        public SBoBEnforcingOrchestrator(
            IContainerizedPluginOrchestrator inner,
            ISBoBPackageVerifier verifier,
            SBoBEnforcingOrchestratorOptions? options = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _options = options ?? new SBoBEnforcingOrchestratorOptions();
        }

        /// <inheritdoc />
        public async Task<PluginDeploymentResult> DeployPluginAsync(
            PluginDeploymentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_options.Mode == SBoBEnforcementMode.Off)
            {
                return await _inner.DeployPluginAsync(request, cancellationToken).ConfigureAwait(false);
            }

            SBoBVerificationResult verification;
            try
            {
                verification = await _verifier.VerifyAsync(
                    request.PackageStream,
                    BuildOptions(request),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail closed: verification infrastructure failure denies the deployment in Enforce mode.
                verification = new SBoBVerificationResult
                {
                    Violations = new[]
                    {
                        new SBoBViolation("SBoBVerificationFailed", SBoBViolationSeverity.Error, ex.Message)
                    }
                };
            }

            if (!verification.Allowed && _options.Mode == SBoBEnforcementMode.Enforce)
            {
                return new PluginDeploymentResult
                {
                    Success = false,
                    Errors = new[] { "SBoB enforcement denied the deployment:" }
                        .Concat(verification.ErrorMessages)
                        .ToArray(),
                    Warnings = verification.WarningMessages.ToArray()
                };
            }

            if (_options.Mode == SBoBEnforcementMode.Enforce && verification.Profile is not null)
            {
                TightenNetworkPolicy(request, verification.Profile);
            }

            var result = await _inner.DeployPluginAsync(request, cancellationToken).ConfigureAwait(false);

            if (_options.Mode == SBoBEnforcementMode.Audit && verification.Violations.Count > 0)
            {
                result.Warnings = result.Warnings.Concat(verification.Violations.Select(v => v.ToString())).ToArray();
            }

            return result;
        }

        /// <summary>Tighten (never loosen) the request's NetworkPolicy to the profile's derived policy.</summary>
        private void TightenNetworkPolicy(PluginDeploymentRequest request, SBoBProfile profile)
        {
            var derived = SBoBPolicyEvaluator.DeriveNetworkPolicy(profile);
            if ((int)derived < (int)request.Configuration.NetworkPolicy)
            {
                request.Configuration.NetworkPolicy = derived;
            }
        }

        private SBoBVerificationOptions BuildOptions(PluginDeploymentRequest request)
        {
            string targetOs = _options.TargetOs
                ?? (string.IsNullOrWhiteSpace(request.TargetPlatform)
                    ? "linux"
                    : request.TargetPlatform.Trim().ToLowerInvariant());
            string? expectedPurl = _options.ExpectedPurl ?? ResolvePurlFromMetadata(request.Metadata);
            return new SBoBVerificationOptions
            {
                TargetOs = targetOs,
                ExpectedPurl = expectedPurl,
                Policy = _options.Policy
            };
        }

        /// <summary>Derive pkg:nuget/&lt;id&gt;@&lt;version&gt; from the deployment request's Metadata (convention keys).</summary>
        private static string? ResolvePurlFromMetadata(Dictionary<string, object> metadata)
        {
            if (metadata is null || metadata.Count == 0)
            {
                return null;
            }
            if (!metadata.TryGetValue("PackageId", out var idValue) || idValue is null)
            {
                return null;
            }
            if (!metadata.TryGetValue("PackageVersion", out var versionValue) || versionValue is null)
            {
                return null;
            }
            var packageId = idValue.ToString();
            var version = versionValue switch
            {
                SemanticVersionNumber semanticVersion => semanticVersion.ToString(),
                _ => versionValue.ToString()
            };
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }
            return SBoBPackagePurl.ForNuGet(packageId, version);
        }

        /// <inheritdoc />
        public Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(
            ContainerizedPluginExecutionRequest request,
            CancellationToken cancellationToken = default)
            => _inner.ExecutePluginAsync(request, cancellationToken);

        /// <inheritdoc />
        public Task<ContainerizedPluginInfo?> GetPluginInfoAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default)
            => _inner.GetPluginInfoAsync(pluginId, cancellationToken);

        /// <inheritdoc />
        public Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(
            PluginListOptions? options = null,
            CancellationToken cancellationToken = default)
            => _inner.ListPluginsAsync(options, cancellationToken);

        /// <inheritdoc />
        public Task UndeployPluginAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default)
            => _inner.UndeployPluginAsync(pluginId, cancellationToken);

        /// <inheritdoc />
        public Task<PluginHealth> GetPluginHealthAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default)
            => _inner.GetPluginHealthAsync(pluginId, cancellationToken);

        /// <inheritdoc />
        public Task NotifyPluginUpdateAsync(
            PluginIdentifier pluginId,
            SemanticVersionNumber newVersion,
            CancellationToken cancellationToken = default)
            => _inner.NotifyPluginUpdateAsync(pluginId, newVersion, cancellationToken);

        // Events forward to the inner orchestrator so consumers keep a single subscription point.
#pragma warning disable CS0067 // events are forwarded via add/remove; the backing-field warning does not apply
        /// <inheritdoc />
        public event EventHandler<PluginEventArgs>? PluginDeployed
        {
            add => _inner.PluginDeployed += value;
            remove => _inner.PluginDeployed -= value;
        }

        /// <inheritdoc />
        public event EventHandler<PluginEventArgs>? PluginExecuted
        {
            add => _inner.PluginExecuted += value;
            remove => _inner.PluginExecuted -= value;
        }

        /// <inheritdoc />
        public event EventHandler<PluginEventArgs>? PluginFailed
        {
            add => _inner.PluginFailed += value;
            remove => _inner.PluginFailed -= value;
        }

        /// <inheritdoc />
        public event EventHandler<PluginEventArgs>? PluginUndeployed
        {
            add => _inner.PluginUndeployed += value;
            remove => _inner.PluginUndeployed -= value;
        }
#pragma warning restore CS0067
    }
}
