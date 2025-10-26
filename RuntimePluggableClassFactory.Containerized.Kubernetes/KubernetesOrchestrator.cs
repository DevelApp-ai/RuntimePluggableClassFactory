using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Kubernetes
{
    /// <summary>
    /// Kubernetes implementation of container orchestrator
    /// </summary>
    public class KubernetesOrchestrator : IContainerOrchestrator
    {
        private readonly IKubernetes _client;
        private readonly KubernetesOrchestratorOptions _options;
        private readonly ILogger<KubernetesOrchestrator> _logger;

        public string PlatformName => "Kubernetes";

        public KubernetesOrchestrator(
            IKubernetes client,
            IOptions<KubernetesOrchestratorOptions> options,
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
                var createdPod = await _client.CoreV1.CreateNamespacedPodAsync(
                    pod,
                    _options.Namespace,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Created pod {PodName} in namespace {Namespace}",
                    podName, _options.Namespace);

                // Wait for pod to be ready
                await WaitForPodReadyAsync(podName, cancellationToken);

                return new KubernetesContainerInstance
                {
                    Id = podName,
                    PodName = podName,
                    Namespace = _options.Namespace,
                    CreatedAt = DateTime.UtcNow,
                    Status = ContainerInstanceStatus.Running,
                    Labels = spec.Labels
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pod {PodName}", podName);
                throw new ContainerOrchestrationException($"Failed to create container: {ex.Message}", ex);
            }
        }

        public async Task<ContainerExecutionResult> ExecuteAsync(
            ContainerInstance container,
            ContainerExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (container is not KubernetesContainerInstance k8sContainer)
                throw new ArgumentException("Invalid container instance type");

            try
            {
                var execCommand = BuildExecutionCommand(request);

                // For this basic implementation, we'll simulate execution
                // In a real implementation, you would use the Kubernetes API to execute commands in the pod
                var execResult = await SimulateExecution(k8sContainer, execCommand, cancellationToken);

                return new ContainerExecutionResult
                {
                    Success = execResult.ExitCode == 0,
                    Output = execResult.Output,
                    Error = execResult.Error,
                    ExitCode = execResult.ExitCode,
                    ExecutionTime = execResult.Duration
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command in pod {PodName}",
                    k8sContainer.PodName);

                return new ContainerExecutionResult
                {
                    Success = false,
                    Error = ex.Message,
                    ExitCode = -1
                };
            }
        }

        public async Task<ContainerStatus> GetStatusAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default)
        {
            if (container is not KubernetesContainerInstance k8sContainer)
                throw new ArgumentException("Invalid container instance type");

            try
            {
                var pod = await _client.CoreV1.ReadNamespacedPodAsync(
                    k8sContainer.PodName,
                    k8sContainer.Namespace,
                    cancellationToken: cancellationToken);

                var status = MapPodStatusToContainerStatus(pod.Status?.Phase);

                return new ContainerStatus
                {
                    Status = status,
                    StatusMessage = pod.Status?.Message ?? string.Empty,
                    LastUpdated = DateTime.UtcNow,
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        ["podPhase"] = pod.Status?.Phase ?? "Unknown",
                        ["nodeName"] = pod.Spec?.NodeName ?? string.Empty
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get status for pod {PodName}", k8sContainer.PodName);
                return new ContainerStatus
                {
                    Status = ContainerInstanceStatus.Unknown,
                    StatusMessage = ex.Message,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        public async Task DestroyContainerAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default)
        {
            if (container is not KubernetesContainerInstance k8sContainer)
                throw new ArgumentException("Invalid container instance type");

            try
            {
                await _client.CoreV1.DeleteNamespacedPodAsync(
                    k8sContainer.PodName,
                    k8sContainer.Namespace,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Deleted pod {PodName} from namespace {Namespace}",
                    k8sContainer.PodName, k8sContainer.Namespace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete pod {PodName}", k8sContainer.PodName);
                throw;
            }
        }

        public async Task<IEnumerable<ContainerInstance>> ListContainersAsync(
            ContainerListOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var labelSelector = options?.LabelSelector != null
                    ? string.Join(",", options.LabelSelector.Select(kv => $"{kv.Key}={kv.Value}"))
                    : "crpcf.plugin=true";

                var pods = await _client.CoreV1.ListNamespacedPodAsync(
                    namespaceParameter: _options.Namespace,
                    labelSelector: labelSelector,
                    cancellationToken: cancellationToken);

                return pods.Items.Select(pod => new KubernetesContainerInstance
                {
                    Id = pod.Metadata.Name,
                    PodName = pod.Metadata.Name,
                    Namespace = pod.Metadata.NamespaceProperty,
                    CreatedAt = pod.Metadata.CreationTimestamp?.DateTime ?? DateTime.MinValue,
                    Status = MapPodStatusToContainerStatus(pod.Status?.Phase),
                    Labels = pod.Metadata.Labels?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>()
                }).Cast<ContainerInstance>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list containers");
                return Array.Empty<ContainerInstance>();
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
                    var pod = await _client.CoreV1.ReadNamespacedPodAsync(
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
                        throw new ContainerOrchestrationException($"Pod {podName} failed to start: {pod.Status?.Phase}");
                    }
                }
                catch (Exception ex) when (!(ex is ContainerOrchestrationException))
                {
                    _logger.LogWarning(ex, "Error checking pod status, attempt {Attempt}", attempt + 1);
                }

                await Task.Delay(delayMs, cancellationToken);
            }

            throw new ContainerOrchestrationException($"Timeout waiting for pod {podName} to become ready");
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

        private static string[] BuildExecutionCommand(ContainerExecutionRequest request)
        {
            // This would be customized based on the plugin execution protocol
            return new[]
            {
                "/bin/sh",
                "-c",
                $"echo '{request.InputData}' | /app/plugin-executor"
            };
        }

        private async Task<MockExecutionResult> SimulateExecution(
            KubernetesContainerInstance container,
            string[] command,
            CancellationToken cancellationToken)
        {
            // This is a simulation for the basic implementation
            // In a real implementation, you would use the Kubernetes API to execute commands
            await Task.Delay(100, cancellationToken); // Simulate execution time

            return new MockExecutionResult
            {
                Output = "Plugin executed successfully",
                Error = string.Empty,
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(100)
            };
        }

        private static ContainerInstanceStatus MapPodStatusToContainerStatus(string? podPhase)
        {
            return podPhase switch
            {
                "Pending" => ContainerInstanceStatus.Creating,
                "Running" => ContainerInstanceStatus.Running,
                "Succeeded" => ContainerInstanceStatus.Stopped,
                "Failed" => ContainerInstanceStatus.Failed,
                _ => ContainerInstanceStatus.Unknown
            };
        }
    }

    /// <summary>
    /// Kubernetes container instance
    /// </summary>
    public class KubernetesContainerInstance : ContainerInstance
    {
        public string PodName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public override string Platform => "Kubernetes";
    }

    /// <summary>
    /// Kubernetes orchestrator configuration options
    /// </summary>
    public class KubernetesOrchestratorOptions
    {
        public string Namespace { get; set; } = "crpcf-plugins";
        public string ServiceAccount { get; set; } = "crpcf-sa";
        public string? ImagePullSecret { get; set; }
        public Dictionary<string, string> NodeSelector { get; set; } = new();
        public Dictionary<string, string> Tolerations { get; set; } = new();
    }

    /// <summary>
    /// Container orchestration exception
    /// </summary>
    public class ContainerOrchestrationException : Exception
    {
        public ContainerOrchestrationException(string message) : base(message) { }
        public ContainerOrchestrationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Mock execution result for simulation
    /// </summary>
    internal class MockExecutionResult
    {
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
    }
}