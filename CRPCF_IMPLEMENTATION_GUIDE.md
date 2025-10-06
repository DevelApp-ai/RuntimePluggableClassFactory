# CRPCF Implementation Guide

## Overview

This guide provides detailed implementation specifications for building the Containerized RuntimePluggableClassFactory (CRPCF) based on the Technical Design Specification.

## 1. Core Interface Definitions

### 1.1 Package and Signature Management

```csharp
// File: CRPCF.Core/Interfaces/ISignedPackageManager.cs
namespace CRPCF.Core.Interfaces
{
    public interface ISignedPackageManager
    {
        Task<PackageValidationResult> ValidatePackageAsync(
            Stream packageStream, 
            PackageValidationOptions options);
        
        Task<SignedPackageInfo> ExtractPackageInfoAsync(Stream packageStream);
        
        Task<bool> IsSignerTrustedAsync(SignerInfo signer, string packageId);
        
        Task<IEnumerable<TrustedSigner>> GetTrustedSignersAsync();
        
        Task AddTrustedSignerAsync(TrustedSigner signer);
        
        Task RemoveTrustedSignerAsync(string signerThumbprint);
    }
    
    public class PackageValidationResult
    {
        public bool IsValid { get; set; }
        public SignedPackageInfo PackageInfo { get; set; }
        public IEnumerable<ValidationError> Errors { get; set; }
        public IEnumerable<ValidationWarning> Warnings { get; set; }
    }
    
    public class SignedPackageInfo
    {
        public string PackageId { get; set; }
        public string Version { get; set; }
        public SignerInfo SignerInfo { get; set; }
        public DateTime SignedAt { get; set; }
        public byte[] PackageHash { get; set; }
        public long PackageSize { get; set; }
    }
    
    public class SignerInfo
    {
        public string SubjectName { get; set; }
        public string CertificateThumbprint { get; set; }
        public X509Certificate2 Certificate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public bool IsExpired => DateTime.UtcNow > ValidTo;
    }
}
```

### 1.2 Container Orchestration Abstraction

```csharp
// File: CRPCF.Core/Interfaces/IContainerOrchestrator.cs
namespace CRPCF.Core.Interfaces
{
    public interface IContainerOrchestrator
    {
        string PlatformName { get; }
        
        Task<ContainerInstance> CreateContainerAsync(
            ContainerSpec spec,
            CancellationToken cancellationToken = default);
        
        Task<ExecutionResult> ExecuteAsync(
            ContainerInstance container,
            ExecutionRequest request,
            CancellationToken cancellationToken = default);
        
        Task<ContainerStatus> GetStatusAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default);
        
        Task DestroyContainerAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default);
        
        Task<IEnumerable<ContainerInstance>> ListContainersAsync(
            ContainerListOptions options = null,
            CancellationToken cancellationToken = default);
    }
    
    public class ContainerSpec
    {
        public string ImageName { get; set; }
        public string ImageTag { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new();
        public ContainerResources Resources { get; set; } = new();
        public ContainerSecurity Security { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
    
    public class ContainerResources
    {
        public long MemoryLimitBytes { get; set; } = 512 * 1024 * 1024; // 512MB
        public double CpuLimit { get; set; } = 0.5; // 0.5 CPU cores
        public long DiskLimitBytes { get; set; } = 1024 * 1024 * 1024; // 1GB
    }
    
    public class ContainerSecurity
    {
        public bool ReadOnlyFileSystem { get; set; } = true;
        public bool RunAsNonRoot { get; set; } = true;
        public bool AllowPrivilegeEscalation { get; set; } = false;
        public NetworkPolicy NetworkPolicy { get; set; } = NetworkPolicy.Isolated;
        public IEnumerable<string> AllowedCapabilities { get; set; } = Array.Empty<string>();
    }
    
    public enum NetworkPolicy
    {
        Isolated,        // No network access
        Internal,        // Internal cluster/network only
        Restricted,      // Limited external access
        Unrestricted     // Full network access
    }
}
```

### 1.3 Plugin Orchestrator Interface

```csharp
// File: CRPCF.Core/Interfaces/IPluginOrchestrator.cs
namespace CRPCF.Core.Interfaces
{
    public interface IPluginOrchestrator
    {
        Task<PluginDeploymentResult> DeployPluginAsync(
            DeploymentRequest request,
            CancellationToken cancellationToken = default);
        
        Task<PluginExecutionResult> ExecutePluginAsync(
            PluginExecutionRequest request,
            CancellationToken cancellationToken = default);
        
        Task<PluginInfo> GetPluginInfoAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);
        
        Task<IEnumerable<PluginInfo>> ListPluginsAsync(
            PluginListOptions options = null,
            CancellationToken cancellationToken = default);
        
        Task UndeployPluginAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);
        
        Task<PluginHealth> GetPluginHealthAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);
        
        event EventHandler<PluginEventArgs> PluginDeployed;
        event EventHandler<PluginEventArgs> PluginExecuted;
        event EventHandler<PluginEventArgs> PluginFailed;
        event EventHandler<PluginEventArgs> PluginUndeployed;
    }
    
    public class DeploymentRequest
    {
        public Stream PackageStream { get; set; }
        public string TargetPlatform { get; set; }
        public DeploymentConfiguration Configuration { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public class PluginExecutionRequest
    {
        public PluginIdentifier PluginId { get; set; }
        public string InputData { get; set; }
        public ExecutionConfiguration Configuration { get; set; } = new();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    }
    
    public class PluginIdentifier
    {
        public string Namespace { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        
        public override string ToString() => $"{Namespace}.{Name}@{Version}";
    }
}
```

## 2. Implementation Classes

### 2.1 Kubernetes Container Orchestrator Implementation

```csharp
// File: CRPCF.Orchestrators.Kubernetes/KubernetesOrchestrator.cs
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRPCF.Orchestrators.Kubernetes
{
    public class KubernetesOrchestrator : IContainerOrchestrator
    {
        private readonly IKubernetesClient _client;
        private readonly KubernetesOptions _options;
        private readonly ILogger<KubernetesOrchestrator> _logger;
        
        public string PlatformName => "Kubernetes";
        
        public KubernetesOrchestrator(
            IKubernetesClient client,
            IOptions<KubernetesOptions> options,
            ILogger<KubernetesOrchestrator> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<ContainerInstance> CreateContainerAsync(
            ContainerSpec spec,
            CancellationToken cancellationToken = default)
        {
            var podName = GeneratePodName(spec);
            var pod = CreatePodDefinition(spec, podName);
            
            try
            {
                var createdPod = await _client.CreateNamespacedPodAsync(
                    pod, 
                    _options.Namespace,
                    cancellationToken: cancellationToken);
                
                _logger.LogInformation("Created pod {PodName} in namespace {Namespace}", 
                    podName, _options.Namespace);
                
                // Wait for pod to be ready
                await WaitForPodReadyAsync(podName, cancellationToken);
                
                return new KubernetesContainerInstance
                {
                    PodName = podName,
                    Namespace = _options.Namespace,
                    CreatedAt = DateTime.UtcNow,
                    Status = ContainerStatus.Running,
                    Labels = spec.Labels
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pod {PodName}", podName);
                throw new ContainerOrchestrationException(
                    $"Failed to create container: {ex.Message}", ex);
            }
        }
        
        public async Task<ExecutionResult> ExecuteAsync(
            ContainerInstance container,
            ExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (container is not KubernetesContainerInstance k8sContainer)
                throw new ArgumentException("Invalid container instance type");
                
            try
            {
                var execCommand = BuildExecutionCommand(request);
                
                var execResult = await _client.NamespacedPodExecAsync(
                    k8sContainer.PodName,
                    k8sContainer.Namespace,
                    "plugin", // container name
                    execCommand,
                    tty: false,
                    cancellationToken: cancellationToken);
                
                return new ExecutionResult
                {
                    Success = execResult.ExitCode == 0,
                    Output = execResult.StandardOutput,
                    Error = execResult.StandardError,
                    ExitCode = execResult.ExitCode,
                    ExecutionTime = execResult.ExecutionTime
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command in pod {PodName}", 
                    k8sContainer.PodName);
                
                return new ExecutionResult
                {
                    Success = false,
                    Error = ex.Message,
                    ExitCode = -1
                };
            }
        }
        
        private V1Pod CreatePodDefinition(ContainerSpec spec, string podName)
        {
            return new V1Pod
            {
                Metadata = new V1ObjectMeta
                {
                    Name = podName,
                    NamespaceProperty = _options.Namespace,
                    Labels = new Dictionary<string, string>(spec.Labels)
                    {
                        ["crpcf.plugin"] = "true",
                        ["crpcf.created-by"] = "crpcf-orchestrator"
                    }
                },
                Spec = new V1PodSpec
                {
                    RestartPolicy = "Never",
                    ServiceAccountName = _options.ServiceAccount,
                    SecurityContext = new V1PodSecurityContext
                    {
                        RunAsNonRoot = spec.Security.RunAsNonRoot,
                        FsGroup = 65534 // nobody group
                    },
                    Containers = new List<V1Container>
                    {
                        new V1Container
                        {
                            Name = "plugin",
                            Image = $"{spec.ImageName}:{spec.ImageTag}",
                            ImagePullPolicy = "Always",
                            Env = spec.EnvironmentVariables.Select(kv => 
                                new V1EnvVar { Name = kv.Key, Value = kv.Value }).ToList(),
                            Resources = new V1ResourceRequirements
                            {
                                Limits = new Dictionary<string, ResourceQuantity>
                                {
                                    ["memory"] = new ResourceQuantity($"{spec.Resources.MemoryLimitBytes}"),
                                    ["cpu"] = new ResourceQuantity($"{spec.Resources.CpuLimit}")
                                },
                                Requests = new Dictionary<string, ResourceQuantity>
                                {
                                    ["memory"] = new ResourceQuantity($"{spec.Resources.MemoryLimitBytes / 2}"),
                                    ["cpu"] = new ResourceQuantity($"{spec.Resources.CpuLimit / 2}")
                                }
                            },
                            SecurityContext = new V1SecurityContext
                            {
                                ReadOnlyRootFilesystem = spec.Security.ReadOnlyFileSystem,
                                RunAsNonRoot = spec.Security.RunAsNonRoot,
                                AllowPrivilegeEscalation = spec.Security.AllowPrivilegeEscalation,
                                Capabilities = new V1Capabilities
                                {
                                    Drop = new List<string> { "ALL" },
                                    Add = spec.Security.AllowedCapabilities.ToList()
                                }
                            }
                        }
                    }
                }
            };
        }
        
        private async Task WaitForPodReadyAsync(string podName, CancellationToken cancellationToken)
        {
            const int maxAttempts = 30;
            const int delayMs = 2000;
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var pod = await _client.ReadNamespacedPodAsync(
                        podName, 
                        _options.Namespace,
                        cancellationToken: cancellationToken);
                    
                    if (IsPodReady(pod))
                    {
                        _logger.LogInformation("Pod {PodName} is ready", podName);
                        return;
                    }
                    
                    if (IsPodFailed(pod))
                    {
                        throw new ContainerOrchestrationException(
                            $"Pod {podName} failed to start: {pod.Status.Phase}");
                    }
                }
                catch (Exception ex) when (!(ex is ContainerOrchestrationException))
                {
                    _logger.LogWarning(ex, "Error checking pod status, attempt {Attempt}", attempt + 1);
                }
                
                await Task.Delay(delayMs, cancellationToken);
            }
            
            throw new ContainerOrchestrationException(
                $"Timeout waiting for pod {podName} to become ready");
        }
        
        private static bool IsPodReady(V1Pod pod)
        {
            return pod.Status?.Conditions?.Any(c => 
                c.Type == "Ready" && c.Status == "True") == true;
        }
        
        private static bool IsPodFailed(V1Pod pod)
        {
            return pod.Status?.Phase == "Failed";
        }
        
        private static string GeneratePodName(ContainerSpec spec)
        {
            var prefix = spec.Labels.TryGetValue("crpcf.plugin-name", out var pluginName) 
                ? $"plugin-{pluginName}" 
                : "plugin";
            
            return $"{prefix}-{Guid.NewGuid():N}"[..63]; // K8s name limit
        }
        
        private static string[] BuildExecutionCommand(ExecutionRequest request)
        {
            // This would be customized based on the plugin execution protocol
            return new[]
            {
                "/bin/sh",
                "-c",
                $"echo '{request.InputData}' | /app/plugin-executor"
            };
        }
    }
    
    public class KubernetesOptions
    {
        public string Namespace { get; set; } = "crpcf-plugins";
        public string ServiceAccount { get; set; } = "crpcf-sa";
        public string ImagePullSecret { get; set; }
        public Dictionary<string, string> NodeSelector { get; set; } = new();
        public Dictionary<string, string> Tolerations { get; set; } = new();
    }
    
    public class KubernetesContainerInstance : ContainerInstance
    {
        public string PodName { get; set; }
        public string Namespace { get; set; }
    }
}
```

### 2.2 NuGet Signature Validator Implementation

```csharp
// File: CRPCF.Security/NuGetSignatureValidator.cs
using System.Security.Cryptography.X509Certificates;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace CRPCF.Security
{
    public class NuGetSignatureValidator : ISignedPackageManager
    {
        private readonly ILogger<NuGetSignatureValidator> _logger;
        private readonly ITrustedSignersRepository _trustedSignersRepo;
        private readonly SignatureValidationSettings _settings;
        
        public NuGetSignatureValidator(
            ILogger<NuGetSignatureValidator> logger,
            ITrustedSignersRepository trustedSignersRepo,
            IOptions<SignatureValidationSettings> settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trustedSignersRepo = trustedSignersRepo ?? throw new ArgumentNullException(nameof(trustedSignersRepo));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }
        
        public async Task<PackageValidationResult> ValidatePackageAsync(
            Stream packageStream, 
            PackageValidationOptions options)
        {
            var result = new PackageValidationResult();
            
            try
            {
                // Reset stream position
                packageStream.Seek(0, SeekOrigin.Begin);
                
                using var package = new PackageArchiveReader(packageStream);
                
                // Extract package information
                result.PackageInfo = await ExtractPackageInfoInternalAsync(package);
                
                // Validate package signature
                var signatureValidation = await ValidateSignatureAsync(package);
                if (!signatureValidation.IsValid)
                {
                    result.IsValid = false;
                    result.Errors = signatureValidation.Errors;
                    return result;
                }
                
                // Check if signer is trusted
                var signerValidation = await ValidateSignerTrustAsync(
                    signatureValidation.SignerInfo, 
                    result.PackageInfo.PackageId);
                
                if (!signerValidation.IsValid)
                {
                    result.IsValid = false;
                    result.Errors = result.Errors.Concat(signerValidation.Errors);
                    return result;
                }
                
                // Validate package integrity
                var integrityValidation = await ValidatePackageIntegrityAsync(package);
                if (!integrityValidation.IsValid)
                {
                    result.IsValid = false;
                    result.Errors = result.Errors.Concat(integrityValidation.Errors);
                    return result;
                }
                
                result.IsValid = true;
                result.Warnings = signatureValidation.Warnings
                    .Concat(signerValidation.Warnings)
                    .Concat(integrityValidation.Warnings);
                
                _logger.LogInformation("Package {PackageId} v{Version} validated successfully", 
                    result.PackageInfo.PackageId, result.PackageInfo.Version);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating package");
                
                return new PackageValidationResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError($"Validation error: {ex.Message}") }
                };
            }
        }
        
        public async Task<SignedPackageInfo> ExtractPackageInfoAsync(Stream packageStream)
        {
            packageStream.Seek(0, SeekOrigin.Begin);
            using var package = new PackageArchiveReader(packageStream);
            
            return await ExtractPackageInfoInternalAsync(package);
        }
        
        private async Task<SignedPackageInfo> ExtractPackageInfoInternalAsync(PackageArchiveReader package)
        {
            var identity = package.GetIdentity();
            var nuspec = await package.GetNuspecAsync(CancellationToken.None);
            
            // Calculate package hash
            var packageBytes = await ReadAllBytesAsync(package);
            var hash = SHA256.HashData(packageBytes);
            
            return new SignedPackageInfo
            {
                PackageId = identity.Id,
                Version = identity.Version.ToString(),
                PackageHash = hash,
                PackageSize = packageBytes.Length,
                // SignerInfo will be populated during signature validation
            };
        }
        
        private async Task<SignatureValidationResult> ValidateSignatureAsync(PackageArchiveReader package)
        {
            try
            {
                var signatures = await package.GetSignaturesAsync(CancellationToken.None);
                
                if (!signatures.Any())
                {
                    return new SignatureValidationResult
                    {
                        IsValid = false,
                        Errors = new[] { new ValidationError("Package is not signed") }
                    };
                }
                
                foreach (var signature in signatures)
                {
                    var validationResult = await ValidateIndividualSignatureAsync(signature);
                    if (validationResult.IsValid)
                    {
                        return validationResult; // First valid signature wins
                    }
                }
                
                return new SignatureValidationResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError("No valid signatures found") }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during signature validation");
                
                return new SignatureValidationResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError($"Signature validation error: {ex.Message}") }
                };
            }
        }
        
        private async Task<SignatureValidationResult> ValidateIndividualSignatureAsync(Signature signature)
        {
            var result = new SignatureValidationResult();
            
            try
            {
                // Validate signature integrity
                var verificationResult = signature.Verify();
                if (verificationResult.Status != SignatureVerificationStatus.Valid)
                {
                    result.IsValid = false;
                    result.Errors = new[] { new ValidationError($"Signature verification failed: {verificationResult.Status}") };
                    return result;
                }
                
                // Extract signer information
                var primarySignature = signature.PrimarySignature;
                var certificate = primarySignature.SignerInfo.Certificate;
                
                result.SignerInfo = new SignerInfo
                {
                    SubjectName = certificate.Subject,
                    CertificateThumbprint = certificate.Thumbprint,
                    Certificate = certificate,
                    ValidFrom = certificate.NotBefore,
                    ValidTo = certificate.NotAfter
                };
                
                // Check certificate validity
                if (result.SignerInfo.IsExpired)
                {
                    result.IsValid = false;
                    result.Errors = new[] { new ValidationError("Signing certificate has expired") };
                    return result;
                }
                
                // Additional certificate chain validation
                if (_settings.ValidateCertificateChain)
                {
                    var chainValidation = ValidateCertificateChain(certificate);
                    if (!chainValidation.IsValid)
                    {
                        result.Warnings = chainValidation.Warnings;
                        if (_settings.RequireValidCertificateChain)
                        {
                            result.IsValid = false;
                            result.Errors = chainValidation.Errors;
                            return result;
                        }
                    }
                }
                
                result.IsValid = true;
                return result;
            }
            catch (Exception ex)
            {
                return new SignatureValidationResult
                {
                    IsValid = false,
                    Errors = new[] { new ValidationError($"Signature validation error: {ex.Message}") }
                };
            }
        }
        
        public async Task<bool> IsSignerTrustedAsync(SignerInfo signer, string packageId)
        {
            var trustedSigners = await _trustedSignersRepo.GetTrustedSignersAsync();
            
            var matchingSigner = trustedSigners.FirstOrDefault(ts =>
                ts.CertificateThumbprint.Equals(signer.CertificateThumbprint, StringComparison.OrdinalIgnoreCase));
            
            if (matchingSigner == null)
            {
                _logger.LogWarning("Signer {SubjectName} ({Thumbprint}) not found in trusted signers list",
                    signer.SubjectName, signer.CertificateThumbprint);
                return false;
            }
            
            // Check if package matches allowed patterns
            if (matchingSigner.AllowedPackagePatterns?.Any() == true)
            {
                var isPackageAllowed = matchingSigner.AllowedPackagePatterns.Any(pattern =>
                    IsPackageMatchingPattern(packageId, pattern));
                
                if (!isPackageAllowed)
                {
                    _logger.LogWarning("Package {PackageId} not allowed for signer {SignerName}",
                        packageId, matchingSigner.SignerName);
                    return false;
                }
            }
            
            _logger.LogInformation("Signer {SignerName} is trusted for package {PackageId}",
                matchingSigner.SignerName, packageId);
            
            return true;
        }
        
        private static bool IsPackageMatchingPattern(string packageId, string pattern)
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$";
            return Regex.IsMatch(packageId, regexPattern, RegexOptions.IgnoreCase);
        }
        
        // Additional helper methods...
    }
    
    public class SignatureValidationSettings
    {
        public bool ValidateCertificateChain { get; set; } = true;
        public bool RequireValidCertificateChain { get; set; } = false;
        public bool CheckCertificateRevocation { get; set; } = true;
        public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool AllowSelfSignedCertificates { get; set; } = false;
    }
}
```

### 2.3 Main Plugin Orchestrator Implementation

```csharp
// File: CRPCF.Core/PluginOrchestrator.cs
namespace CRPCF.Core
{
    public class PluginOrchestrator : IPluginOrchestrator
    {
        private readonly ISignedPackageManager _packageManager;
        private readonly IEnumerable<IContainerOrchestrator> _orchestrators;
        private readonly IPluginRepository _pluginRepository;
        private readonly ILogger<PluginOrchestrator> _logger;
        private readonly OrchestratorOptions _options;
        
        private readonly ConcurrentDictionary<string, PluginDeployment> _deployments = new();
        
        public event EventHandler<PluginEventArgs> PluginDeployed;
        public event EventHandler<PluginEventArgs> PluginExecuted;
        public event EventHandler<PluginEventArgs> PluginFailed;
        public event EventHandler<PluginEventArgs> PluginUndeployed;
        
        public PluginOrchestrator(
            ISignedPackageManager packageManager,
            IEnumerable<IContainerOrchestrator> orchestrators,
            IPluginRepository pluginRepository,
            ILogger<PluginOrchestrator> logger,
            IOptions<OrchestratorOptions> options)
        {
            _packageManager = packageManager ?? throw new ArgumentNullException(nameof(packageManager));
            _orchestrators = orchestrators ?? throw new ArgumentNullException(nameof(orchestrators));
            _pluginRepository = pluginRepository ?? throw new ArgumentNullException(nameof(pluginRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }
        
        public async Task<PluginDeploymentResult> DeployPluginAsync(
            DeploymentRequest request,
            CancellationToken cancellationToken = default)
        {
            var deploymentId = Guid.NewGuid().ToString();
            
            try
            {
                _logger.LogInformation("Starting plugin deployment {DeploymentId}", deploymentId);
                
                // Step 1: Validate package signature
                var validationResult = await _packageManager.ValidatePackageAsync(
                    request.PackageStream, 
                    new PackageValidationOptions());
                
                if (!validationResult.IsValid)
                {
                    var errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                    _logger.LogError("Package validation failed for deployment {DeploymentId}: {Errors}",
                        deploymentId, errorMessage);
                    
                    return new PluginDeploymentResult
                    {
                        Success = false,
                        DeploymentId = deploymentId,
                        Errors = validationResult.Errors.Select(e => e.Message).ToList()
                    };
                }
                
                var packageInfo = validationResult.PackageInfo;
                var pluginId = new PluginIdentifier
                {
                    Namespace = packageInfo.PackageId.Split('.')[0],
                    Name = packageInfo.PackageId,
                    Version = packageInfo.Version
                };
                
                // Step 2: Store package
                await _pluginRepository.StorePackageAsync(request.PackageStream, packageInfo);
                
                // Step 3: Select container orchestrator
                var orchestrator = GetOrchestrator(request.TargetPlatform);
                
                // Step 4: Build container specification
                var containerSpec = BuildContainerSpec(packageInfo, request.Configuration);
                
                // Step 5: Deploy container
                var container = await orchestrator.CreateContainerAsync(containerSpec, cancellationToken);
                
                // Step 6: Store deployment information
                var deployment = new PluginDeployment
                {
                    DeploymentId = deploymentId,
                    PluginId = pluginId,
                    PackageInfo = packageInfo,
                    Container = container,
                    Orchestrator = orchestrator,
                    DeployedAt = DateTime.UtcNow,
                    Status = PluginDeploymentStatus.Deployed
                };
                
                _deployments[pluginId.ToString()] = deployment;
                await _pluginRepository.SaveDeploymentAsync(deployment);
                
                _logger.LogInformation("Plugin {PluginId} deployed successfully as {DeploymentId}",
                    pluginId, deploymentId);
                
                // Raise event
                PluginDeployed?.Invoke(this, new PluginEventArgs { PluginId = pluginId, DeploymentId = deploymentId });
                
                return new PluginDeploymentResult
                {
                    Success = true,
                    DeploymentId = deploymentId,
                    PluginId = pluginId,
                    ContainerInfo = new ContainerInfo
                    {
                        Platform = orchestrator.PlatformName,
                        InstanceId = container.Id,
                        Status = container.Status.ToString()
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy plugin for deployment {DeploymentId}", deploymentId);
                
                PluginFailed?.Invoke(this, new PluginEventArgs 
                { 
                    DeploymentId = deploymentId, 
                    Error = ex.Message 
                });
                
                return new PluginDeploymentResult
                {
                    Success = false,
                    DeploymentId = deploymentId,
                    Errors = new[] { ex.Message }
                };
            }
        }
        
        public async Task<PluginExecutionResult> ExecutePluginAsync(
            PluginExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var executionId = Guid.NewGuid().ToString();
            
            try
            {
                _logger.LogInformation("Starting plugin execution {ExecutionId} for plugin {PluginId}",
                    executionId, request.PluginId);
                
                // Find deployment
                if (!_deployments.TryGetValue(request.PluginId.ToString(), out var deployment))
                {
                    return new PluginExecutionResult
                    {
                        Success = false,
                        ExecutionId = executionId,
                        Error = $"Plugin {request.PluginId} is not deployed"
                    };
                }
                
                // Check container health
                var containerStatus = await deployment.Orchestrator.GetStatusAsync(
                    deployment.Container, cancellationToken);
                
                if (containerStatus.Status != ContainerStatus.Running)
                {
                    return new PluginExecutionResult
                    {
                        Success = false,
                        ExecutionId = executionId,
                        Error = $"Container is not running. Status: {containerStatus.Status}"
                    };
                }
                
                // Execute plugin
                var executionRequest = new ExecutionRequest
                {
                    InputData = request.InputData,
                    Configuration = request.Configuration,
                    Timeout = request.Timeout
                };
                
                var startTime = DateTime.UtcNow;
                var executionResult = await deployment.Orchestrator.ExecuteAsync(
                    deployment.Container,
                    executionRequest,
                    cancellationToken);
                var endTime = DateTime.UtcNow;
                
                var result = new PluginExecutionResult
                {
                    Success = executionResult.Success,
                    ExecutionId = executionId,
                    Output = executionResult.Output,
                    Error = executionResult.Error,
                    ExecutionTime = endTime - startTime,
                    ExitCode = executionResult.ExitCode
                };
                
                _logger.LogInformation("Plugin execution {ExecutionId} completed. Success: {Success}, Duration: {Duration}ms",
                    executionId, result.Success, result.ExecutionTime.TotalMilliseconds);
                
                // Raise event
                PluginExecuted?.Invoke(this, new PluginEventArgs 
                { 
                    PluginId = request.PluginId, 
                    ExecutionId = executionId,
                    Success = result.Success
                });
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute plugin {PluginId} for execution {ExecutionId}",
                    request.PluginId, executionId);
                
                PluginFailed?.Invoke(this, new PluginEventArgs
                {
                    PluginId = request.PluginId,
                    ExecutionId = executionId,
                    Error = ex.Message
                });
                
                return new PluginExecutionResult
                {
                    Success = false,
                    ExecutionId = executionId,
                    Error = ex.Message
                };
            }
        }
        
        private IContainerOrchestrator GetOrchestrator(string targetPlatform)
        {
            var orchestrator = _orchestrators.FirstOrDefault(o => 
                o.PlatformName.Equals(targetPlatform, StringComparison.OrdinalIgnoreCase));
            
            if (orchestrator == null)
            {
                orchestrator = _orchestrators.FirstOrDefault(o =>
                    o.PlatformName.Equals(_options.DefaultPlatform, StringComparison.OrdinalIgnoreCase));
            }
            
            return orchestrator ?? _orchestrators.First();
        }
        
        private ContainerSpec BuildContainerSpec(SignedPackageInfo packageInfo, DeploymentConfiguration config)
        {
            return new ContainerSpec
            {
                ImageName = _options.BaseImageName,
                ImageTag = _options.BaseImageTag,
                Labels = new Dictionary<string, string>
                {
                    ["crpcf.plugin-id"] = packageInfo.PackageId,
                    ["crpcf.plugin-version"] = packageInfo.Version,
                    ["crpcf.deployment-time"] = DateTime.UtcNow.ToString("O")
                },
                Resources = new ContainerResources
                {
                    MemoryLimitBytes = config.MemoryLimitMB * 1024 * 1024,
                    CpuLimit = config.CpuLimit,
                    DiskLimitBytes = config.DiskLimitMB * 1024 * 1024
                },
                Security = new ContainerSecurity
                {
                    ReadOnlyFileSystem = config.ReadOnlyFileSystem,
                    RunAsNonRoot = true,
                    AllowPrivilegeEscalation = false,
                    NetworkPolicy = config.NetworkPolicy
                },
                EnvironmentVariables = config.EnvironmentVariables ?? new Dictionary<string, string>(),
                ExecutionTimeout = TimeSpan.FromMinutes(config.ExecutionTimeoutMinutes)
            };
        }
    }
    
    public class OrchestratorOptions
    {
        public string DefaultPlatform { get; set; } = "Kubernetes";
        public string BaseImageName { get; set; } = "crpcf/plugin-runtime";
        public string BaseImageTag { get; set; } = "latest";
    }
}
```

## 3. Configuration Classes

```csharp
// File: CRPCF.Core/Configuration/CRPCFConfiguration.cs
namespace CRPCF.Core.Configuration
{
    public class CRPCFConfiguration
    {
        public OrchestratorConfiguration Orchestrator { get; set; } = new();
        public SecurityConfiguration Security { get; set; } = new();
        public PluginStoreConfiguration PluginStore { get; set; } = new();
        public ContainerOrchestrationConfiguration ContainerOrchestration { get; set; } = new();
        public MonitoringConfiguration Monitoring { get; set; } = new();
    }
    
    public class SecurityConfiguration
    {
        public bool RequireSignedPackages { get; set; } = true;
        public TimeSpan SignatureValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public string TrustedSignersConfigPath { get; set; } = "/config/trusted-signers.json";
        public bool ValidateCertificateChain { get; set; } = true;
        public bool RequireValidCertificateChain { get; set; } = false;
        public bool CheckCertificateRevocation { get; set; } = true;
    }
    
    public class PluginStoreConfiguration
    {
        public string Type { get; set; } = "filesystem";
        public string Path { get; set; } = "/data/plugins";
        public int MaxSizeGB { get; set; } = 100;
        public CleanupPolicyConfiguration CleanupPolicy { get; set; } = new();
    }
    
    public class CleanupPolicyConfiguration
    {
        public int MaxAgeDays { get; set; } = 30;
        public int MaxVersionsPerPlugin { get; set; } = 5;
    }
}
```

This implementation guide provides a solid foundation for building the Containerized RuntimePluggableClassFactory with all the security, orchestration, and management capabilities outlined in the Technical Design Specification.