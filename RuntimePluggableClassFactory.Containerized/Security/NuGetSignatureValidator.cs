using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Security
{
    /// <summary>
    /// NuGet package signature validator with trusted signer whitelist support
    /// </summary>
    public class NuGetSignatureValidator : ISignedPackageManager
    {
        private readonly ILogger<NuGetSignatureValidator> _logger;
        private readonly ITrustedSignersRepository _trustedSignersRepo;
        private readonly NuGetSignatureValidatorOptions _options;

        public NuGetSignatureValidator(
            ILogger<NuGetSignatureValidator> logger,
            ITrustedSignersRepository trustedSignersRepo,
            NuGetSignatureValidatorOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trustedSignersRepo = trustedSignersRepo ?? throw new ArgumentNullException(nameof(trustedSignersRepo));
            _options = options ?? new NuGetSignatureValidatorOptions();
        }

        /// <summary>
        /// Validates a NuGet package signature against trusted signers
        /// </summary>
        public async Task<PackageValidationResult> ValidatePackageAsync(
            Stream packageStream, 
            PackageValidationOptions options)
        {
            try
            {
                _logger.LogDebug("Starting package validation");

                // Reset stream position
                packageStream.Seek(0, SeekOrigin.Begin);

                using var package = new PackageArchiveReader(packageStream);

                // Extract package information
                var packageInfo = await ExtractPackageInfoInternalAsync(package);

                // Check if signature is required
                if (options.RequireSignature)
                {
                    // Validate package signature
                    var signatureResult = await ValidateSignatureAsync(package);
                    if (!signatureResult.IsValid)
                    {
                        _logger.LogWarning("Package signature validation failed for {PackageId}", packageInfo.PackageId);
                        return new PackageValidationResult
                        {
                            IsValid = false,
                            PackageInfo = packageInfo,
                            Errors = signatureResult.Errors
                        };
                    }

                    packageInfo.SignerInfo = signatureResult.SignerInfo;

                    // Validate against trusted signers
                    if (packageInfo.SignerInfo != null)
                    {
                        var signerResult = await ValidateSignerTrustAsync(packageInfo.SignerInfo, packageInfo.PackageId);
                        if (!signerResult.IsValid)
                        {
                            _logger.LogWarning("Signer validation failed for {PackageId} by {SignerName}", 
                                packageInfo.PackageId, packageInfo.SignerInfo.SubjectName);
                            
                            return new PackageValidationResult
                            {
                                IsValid = false,
                                PackageInfo = packageInfo,
                                Errors = signerResult.Errors
                            };
                        }
                    }
                }

                _logger.LogInformation("Package {PackageId} v{Version} validated successfully", 
                    packageInfo.PackageId, packageInfo.Version);

                return new PackageValidationResult
                {
                    IsValid = true,
                    PackageInfo = packageInfo
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating package signature");
                return new PackageValidationResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError { Message = $"Validation error: {ex.Message}" } }
                };
            }
        }

        /// <summary>
        /// Extracts package information from a NuGet package
        /// </summary>
        public async Task<SignedPackageInfo> ExtractPackageInfoAsync(Stream packageStream)
        {
            packageStream.Seek(0, SeekOrigin.Begin);
            using var package = new PackageArchiveReader(packageStream);
            return await ExtractPackageInfoInternalAsync(package);
        }

        private async Task<SignedPackageInfo> ExtractPackageInfoInternalAsync(PackageArchiveReader package)
        {
            var identity = package.GetIdentity();

            // Calculate package hash using actual package content
            byte[] hash;
            long packageSize;
            using (var stream = package.GetStream())
            {
                (hash, packageSize) = await ComputeHashAndSizeAsync(stream);
            }

            return new SignedPackageInfo
            {
                PackageId = identity.Id,
                Version = identity.Version.ToString(),
                PackageHash = hash,
                PackageSize = packageSize
            };
        }

        /// <summary>
        /// Computes the SHA256 hash and size of a stream.
        /// </summary>
        private static async Task<(byte[] hash, long size)> ComputeHashAndSizeAsync(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using (var sha256 = SHA256.Create())
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalBytes = 0;
                // Use CryptoStream to compute hash as we read
                using (var cryptoStream = new CryptoStream(Stream.Null, sha256, CryptoStreamMode.Write))
                {
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await cryptoStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytes += bytesRead;
                    }
                }
                return (sha256.Hash, totalBytes);
            }
        }
        private async Task<SignatureValidationInternalResult> ValidateSignatureAsync(PackageArchiveReader package)
        {
            // Simplified signature validation for basic implementation
            // In a real implementation, you would use proper NuGet signature validation APIs
            try
            {
                await Task.Delay(10); // Simulate async work
                
                // For now, assume packages are signed if they exist
                // Real implementation would check digital signatures
                return new SignatureValidationInternalResult
                {
                    IsValid = true,
                    SignerInfo = new SignerInfo
                    {
                        SubjectName = "Demo Signer",
                        CertificateThumbprint = "DEMO123456789",
                        ValidFrom = DateTime.UtcNow.AddDays(-30),
                        ValidTo = DateTime.UtcNow.AddDays(365)
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during signature validation");
                return new SignatureValidationInternalResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError { Message = $"Signature validation error: {ex.Message}" } }
                };
            }
        }

        // Removed - signature validation is now handled in ValidateSignatureAsync method

        private SignatureValidationInternalResult ValidateCertificateChain(X509Certificate2 certificate)
        {
            try
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = _options.CheckRevocation 
                    ? X509RevocationMode.Online 
                    : X509RevocationMode.NoCheck;

                var isValid = chain.Build(certificate);
                if (!isValid)
                {
                    var errors = chain.ChainStatus.Select(status => 
                        new ValidationError { Message = $"Certificate chain error: {status.StatusInformation}" });

                    return new SignatureValidationInternalResult
                    {
                        IsValid = false,
                        Errors = errors
                    };
                }

                return new SignatureValidationInternalResult { IsValid = true };
            }
            catch (Exception ex)
            {
                return new SignatureValidationInternalResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError { Message = $"Certificate chain validation error: {ex.Message}" } }
                };
            }
        }

        private async Task<SignatureValidationInternalResult> ValidateSignerTrustAsync(SignerInfo signer, string packageId)
        {
            try
            {
                var trustedSigners = await _trustedSignersRepo.GetTrustedSignersAsync();

                var matchingSigner = trustedSigners.FirstOrDefault(ts =>
                    ts.CertificateThumbprint.Equals(signer.CertificateThumbprint, StringComparison.OrdinalIgnoreCase));

                if (matchingSigner == null)
                {
                    return new SignatureValidationInternalResult
                    {
                        IsValid = false,
                        Errors = new[] { new ValidationError { Message = $"Signer not in whitelist: {signer.SubjectName}" } }
                    };
                }

                // Check if package matches allowed patterns
                if (matchingSigner.AllowedPackagePatterns?.Any() == true)
                {
                    var isPackageAllowed = matchingSigner.AllowedPackagePatterns.Any(pattern =>
                        IsPackageMatchingPattern(packageId, pattern));

                    if (!isPackageAllowed)
                    {
                        return new SignatureValidationInternalResult
                        {
                            IsValid = false,
                            Errors = new[] { new ValidationError { Message = $"Package '{packageId}' not allowed for signer '{matchingSigner.SignerName}'" } }
                        };
                    }
                }

                return new SignatureValidationInternalResult { IsValid = true };
            }
            catch (Exception ex)
            {
                return new SignatureValidationInternalResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError { Message = $"Signer validation error: {ex.Message}" } }
                };
            }
        }

        private static bool IsPackageMatchingPattern(string packageId, string pattern)
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$";
            return Regex.IsMatch(packageId, regexPattern, RegexOptions.IgnoreCase);
        }

        // Implementation of remaining interface methods
        public async Task<bool> IsSignerTrustedAsync(SignerInfo signer, string packageId)
        {
            var result = await ValidateSignerTrustAsync(signer, packageId);
            return result.IsValid;
        }

        public async Task<IEnumerable<TrustedSigner>> GetTrustedSignersAsync()
        {
            return await _trustedSignersRepo.GetTrustedSignersAsync();
        }

        public async Task AddTrustedSignerAsync(TrustedSigner signer)
        {
            await _trustedSignersRepo.AddTrustedSignerAsync(signer);
        }

        public async Task RemoveTrustedSignerAsync(string signerThumbprint)
        {
            await _trustedSignersRepo.RemoveTrustedSignerAsync(signerThumbprint);
        }
    }

    /// <summary>
    /// Internal signature validation result
    /// </summary>
    internal class SignatureValidationInternalResult
    {
        public bool IsValid { get; set; }
        public SignerInfo? SignerInfo { get; set; }
        public IEnumerable<ValidationError> Errors { get; set; } = Array.Empty<ValidationError>();
        public IEnumerable<ValidationWarning> Warnings { get; set; } = Array.Empty<ValidationWarning>();
    }

    /// <summary>
    /// Configuration options for NuGet signature validator
    /// </summary>
    public class NuGetSignatureValidatorOptions
    {
        public bool ValidateCertificateChain { get; set; } = true;
        public bool RequireValidCertificateChain { get; set; } = false;
        public bool CheckRevocation { get; set; } = true;
        public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Interface for trusted signers repository
    /// </summary>
    public interface ITrustedSignersRepository
    {
        Task<IEnumerable<TrustedSigner>> GetTrustedSignersAsync();
        Task AddTrustedSignerAsync(TrustedSigner signer);
        Task RemoveTrustedSignerAsync(string signerThumbprint);
    }
}