using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces
{
    /// <summary>
    /// Interface for managing signed NuGet packages with whitelist validation
    /// </summary>
    public interface ISignedPackageManager
    {
        /// <summary>
        /// Validates a NuGet package signature against trusted signers
        /// </summary>
        /// <param name="packageStream">Package stream</param>
        /// <param name="options">Validation options</param>
        /// <returns>Validation result</returns>
        Task<PackageValidationResult> ValidatePackageAsync(
            Stream packageStream, 
            PackageValidationOptions options);
        
        /// <summary>
        /// Extracts package information from a NuGet package
        /// </summary>
        /// <param name="packageStream">Package stream</param>
        /// <returns>Package information</returns>
        Task<SignedPackageInfo> ExtractPackageInfoAsync(Stream packageStream);
        
        /// <summary>
        /// Checks if a signer is trusted for a specific package
        /// </summary>
        /// <param name="signer">Signer information</param>
        /// <param name="packageId">Package identifier</param>
        /// <returns>True if trusted</returns>
        Task<bool> IsSignerTrustedAsync(SignerInfo signer, string packageId);
        
        /// <summary>
        /// Gets all trusted signers
        /// </summary>
        /// <returns>Trusted signers</returns>
        Task<IEnumerable<TrustedSigner>> GetTrustedSignersAsync();
        
        /// <summary>
        /// Adds a trusted signer
        /// </summary>
        /// <param name="signer">Trusted signer</param>
        /// <returns>Task</returns>
        Task AddTrustedSignerAsync(TrustedSigner signer);
        
        /// <summary>
        /// Removes a trusted signer
        /// </summary>
        /// <param name="signerThumbprint">Signer certificate thumbprint</param>
        /// <returns>Task</returns>
        Task RemoveTrustedSignerAsync(string signerThumbprint);
    }

    /// <summary>
    /// Package validation result
    /// </summary>
    public class PackageValidationResult
    {
        public bool IsValid { get; set; }
        public SignedPackageInfo? PackageInfo { get; set; }
        public IEnumerable<ValidationError> Errors { get; set; } = Array.Empty<ValidationError>();
        public IEnumerable<ValidationWarning> Warnings { get; set; } = Array.Empty<ValidationWarning>();
    }

    /// <summary>
    /// Package validation options
    /// </summary>
    public class PackageValidationOptions
    {
        public bool RequireSignature { get; set; } = true;
        public bool ValidateCertificateChain { get; set; } = true;
        public bool CheckRevocation { get; set; } = true;
        public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Signed package information
    /// </summary>
    public class SignedPackageInfo
    {
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public SignerInfo? SignerInfo { get; set; }
        public DateTime SignedAt { get; set; }
        public byte[] PackageHash { get; set; } = Array.Empty<byte>();
        public long PackageSize { get; set; }
    }

    /// <summary>
    /// Digital signature information
    /// </summary>
    public class SignerInfo
    {
        public string SubjectName { get; set; } = string.Empty;
        public string CertificateThumbprint { get; set; } = string.Empty;
        public X509Certificate2? Certificate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public bool IsExpired => DateTime.UtcNow > ValidTo;
    }

    /// <summary>
    /// Trusted signer configuration
    /// </summary>
    public class TrustedSigner
    {
        public string SignerName { get; set; } = string.Empty;
        public string CertificateThumbprint { get; set; } = string.Empty;
        public X509Certificate2? Certificate { get; set; }
        public SignerTrustLevel TrustLevel { get; set; }
        public IEnumerable<string> AllowedPackagePatterns { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Signer trust levels
    /// </summary>
    public enum SignerTrustLevel
    {
        Restricted,     // Limited package patterns only
        Standard,       // Most packages allowed
        Elevated,       // All packages allowed
        SystemLevel     // System-level packages allowed
    }

    /// <summary>
    /// Validation error
    /// </summary>
    public class ValidationError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    /// <summary>
    /// Validation warning
    /// </summary>
    public class ValidationWarning
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}