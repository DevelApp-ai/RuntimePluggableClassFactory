using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.SBoB
{
    /// <summary>Options for SBoB package verification.</summary>
    public sealed class SBoBVerificationOptions
    {
        /// <summary>Target os of the embedded profile to verify (SBOB-Forge default: linux, org decision #9).</summary>
        public string TargetOs { get; init; } = "linux";

        /// <summary>Expected package purl (pkg:nuget/&lt;lowercased-id&gt;@&lt;version&gt;). When null, the purl binding check is skipped with a warning.</summary>
        public string? ExpectedPurl { get; init; }

        /// <summary>The policy to evaluate against the profile. Defaults to the deny-flavored org defaults.</summary>
        public SBoBPolicy Policy { get; init; } = new();
    }

    /// <summary>
    /// Verifies a plugin package's embedded SBoB profile (org decision #39):
    /// extracts sbob/&lt;package&gt;.&lt;os&gt;.sbob.yaml from the package, parses it
    /// fail-closed against the canonical emission form, checks the purl binding
    /// and the file-name binding, and evaluates the configured policy.
    /// The behavior-hash annotation is treated as an opaque audit value (sha256 over
    /// the normalized IR — it cannot be recomputed from the YAML); tamper-evidence
    /// comes from the package signature and, later, the cosign bundle (SBOB-Forge M6).
    /// </summary>
    public interface ISBoBPackageVerifier
    {
        /// <summary>Verify the package stream. Never throws for package or profile defects — they come back as Error violations. Rethrows only cancellation.</summary>
        Task<SBoBVerificationResult> VerifyAsync(
            Stream packageStream,
            SBoBVerificationOptions? options = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Default hermetic verifier over the package zip (nupkg) stream.</summary>
    public sealed class SBoBPackageVerifier : ISBoBPackageVerifier
    {
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <inheritdoc />
        public async Task<SBoBVerificationResult> VerifyAsync(
            Stream packageStream,
            SBoBVerificationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new SBoBVerificationOptions();
            string targetOs = options.TargetOs ?? "linux";
            var violations = new List<SBoBViolation>();

            try
            {
                using var package = CopyToSeekableStream(packageStream);
                using var archive = new ZipArchive(package, ZipArchiveMode.Read);

                string targetSuffix = $".{targetOs}.sbob.yaml";
                ZipArchiveEntry? match = null;
                int matchCount = 0;
                int otherOsProfiles = 0;

                foreach (var entry in archive.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    if (!path.StartsWith("sbob/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!path.EndsWith(".sbob.yaml", StringComparison.Ordinal))
                    {
                        continue; // provenance sidecar or other data — not a profile
                    }
                    if (path.EndsWith(targetSuffix, StringComparison.Ordinal))
                    {
                        matchCount++;
                        match ??= entry;
                    }
                    else
                    {
                        otherOsProfiles++;
                    }
                }

                if (matchCount > 1)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBMultipleProfiles", SBoBViolationSeverity.Error,
                        $"{matchCount} profiles found for target os '{targetOs}' in sbob/ — exactly one is allowed per (package, target-os)"));
                    return Finish(false, null, null, options.ExpectedPurl, violations);
                }

                if (match is null)
                {
                    if (options.Policy.RequireProfile)
                    {
                        violations.Add(new SBoBViolation(
                            "SBoBProfileRequired", SBoBViolationSeverity.Error,
                            otherOsProfiles > 0
                                ? $"no SBoB profile found for target os '{targetOs}' ({otherOsProfiles} profile(s) exist for other target os)"
                                : $"no SBoB profile found in the package (expected sbob/<package>.{targetOs}.sbob.yaml; org decision #39)"));
                    }
                    return Finish(false, null, null, options.ExpectedPurl, violations);
                }

                var profilePath = match.FullName.Replace('\\', '/');
                string text;
                using (var entryStream = match.Open())
                using (var reader = new StreamReader(entryStream, StrictUtf8, detectEncodingFromByteOrderMarks: false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                }

                SBoBProfile profile;
                try
                {
                    profile = SBoBCanonicalParser.Parse(text);
                }
                catch (SBoBCanonicalFormatException ex)
                {
                    violations.Add(new SBoBViolation(
                        "SBoBCanonicalForm", SBoBViolationSeverity.Error,
                        $"profile '{profilePath}' is not in canonical emission form: {ex.Message} (a file that is not canonical output could hide behavior and is never silently repaired)"));
                    return Finish(false, null, profilePath, options.ExpectedPurl, violations);
                }

                // File-name binding: sbob/<metadata-name>.sbob.yaml (SBOB-Forge TDS §10: metadata.name is <purl-name-with-dashes>.<os>).
                var fileName = Path.GetFileName(profilePath);
                var fileNameBase = fileName[..^".sbob.yaml".Length];
                if (!string.Equals(fileNameBase, profile.MetadataName, StringComparison.Ordinal))
                {
                    violations.Add(new SBoBViolation(
                        "SBoBFileNameBinding", SBoBViolationSeverity.Error,
                        $"profile file is '{fileName}' but its metadata.name is '{profile.MetadataName}' (expected sbob/{profile.MetadataName}.sbob.yaml)"));
                }
                if (!profile.MetadataName.EndsWith($".{targetOs}", StringComparison.Ordinal))
                {
                    violations.Add(new SBoBViolation(
                        "SBoBTargetOsBinding", SBoBViolationSeverity.Error,
                        $"profile metadata.name '{profile.MetadataName}' does not end with the target os '.{targetOs}'"));
                }

                foreach (var violation in SBoBPolicyEvaluator.Evaluate(profile, options.ExpectedPurl, options.Policy))
                {
                    violations.Add(violation);
                }

                return Finish(true, profile, profilePath, options.ExpectedPurl, violations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail closed: an unreadable package or profile can never pass verification.
                violations.Add(new SBoBViolation(
                    "SBoBPackageUnreadable", SBoBViolationSeverity.Error,
                    $"package could not be read for SBoB verification: {ex.Message}"));
                return Finish(false, null, null, options.ExpectedPurl, violations);
            }
        }

        private static SBoBVerificationResult Finish(
            bool found, SBoBProfile? profile, string? path, string? expectedPurl, List<SBoBViolation> violations)
            => new()
            {
                ProfileFound = found,
                Profile = profile,
                ProfilePath = path,
                ExpectedPurl = expectedPurl,
                Violations = violations
            };

        /// <summary>Copy the package stream to a seekable in-memory stream without consuming the caller's position.</summary>
        private static MemoryStream CopyToSeekableStream(Stream source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            var copy = new MemoryStream();
            if (source.CanSeek)
            {
                long originalPosition = source.Position;
                source.Position = 0;
                source.CopyTo(copy);
                source.Position = originalPosition;
            }
            else
            {
                source.CopyTo(copy);
            }
            copy.Position = 0;
            return copy;
        }
    }
}
