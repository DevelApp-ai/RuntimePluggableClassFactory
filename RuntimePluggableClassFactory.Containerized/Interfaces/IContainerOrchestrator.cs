using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces
{
    /// <summary>
    /// Interface for container orchestration platforms (Kubernetes, Azure Container Apps, etc.)
    /// </summary>
    public interface IContainerOrchestrator
    {
        /// <summary>
        /// Name of the container orchestration platform
        /// </summary>
        string PlatformName { get; }

        /// <summary>
        /// Creates a new container instance for a plugin
        /// </summary>
        /// <param name="spec">Container specification</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container instance information</returns>
        Task<ContainerInstance> CreateContainerAsync(
            ContainerSpec spec,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a plugin within a container
        /// </summary>
        /// <param name="container">Container instance</param>
        /// <param name="request">Execution request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Execution result</returns>
        Task<ContainerExecutionResult> ExecuteAsync(
            ContainerInstance container,
            ContainerExecutionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the status of a container
        /// </summary>
        /// <param name="container">Container instance</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container status</returns>
        Task<ContainerStatus> GetStatusAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Destroys a container instance
        /// </summary>
        /// <param name="container">Container instance</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task DestroyContainerAsync(
            ContainerInstance container,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists container instances managed by this orchestrator
        /// </summary>
        /// <param name="options">List options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container instances</returns>
        Task<IEnumerable<ContainerInstance>> ListContainersAsync(
            ContainerListOptions? options = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Container specification for plugin execution
    /// </summary>
    public class ContainerSpec
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImageTag { get; set; } = "latest";
        public Dictionary<string, string> Labels { get; set; } = new();
        public ContainerResources Resources { get; set; } = new();
        public ContainerSecurity Security { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Container resource limits
    /// </summary>
    public class ContainerResources
    {
        public long MemoryLimitBytes { get; set; } = 512 * 1024 * 1024; // 512MB
        public double CpuLimit { get; set; } = 0.5; // 0.5 CPU cores
        public long DiskLimitBytes { get; set; } = 1024 * 1024 * 1024; // 1GB
    }

    /// <summary>
    /// Container security settings
    /// </summary>
    public class ContainerSecurity
    {
        public bool ReadOnlyFileSystem { get; set; } = true;
        public bool RunAsNonRoot { get; set; } = true;
        public bool AllowPrivilegeEscalation { get; set; } = false;
        public NetworkPolicy NetworkPolicy { get; set; } = NetworkPolicy.Isolated;
        public IEnumerable<string> AllowedCapabilities { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Network policy for containers
    /// </summary>
    public enum NetworkPolicy
    {
        Isolated,        // No network access
        Internal,        // Internal cluster/network only
        Restricted,      // Limited external access
        Unrestricted     // Full network access
    }

    /// <summary>
    /// Container instance information
    /// </summary>
    public abstract class ContainerInstance
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public ContainerInstanceStatus Status { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new();
        public abstract string Platform { get; }
    }

    /// <summary>
    /// Container instance status
    /// </summary>
    public enum ContainerInstanceStatus
    {
        Creating,
        Starting,
        Running,
        Stopping,
        Stopped,
        Failed,
        Unknown
    }

    /// <summary>
    /// Container execution request
    /// </summary>
    public class ContainerExecutionRequest
    {
        public string InputData { get; set; } = string.Empty;
        public Dictionary<string, object> ExecutionConfiguration { get; set; } = new();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Container execution result
    /// </summary>
    public class ContainerExecutionResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public TimeSpan ExecutionTime { get; set; }
    }

    /// <summary>
    /// Container status information
    /// </summary>
    public class ContainerStatus
    {
        public ContainerInstanceStatus Status { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    /// <summary>
    /// Container list options
    /// </summary>
    public class ContainerListOptions
    {
        public Dictionary<string, string>? LabelSelector { get; set; }
        public ContainerInstanceStatus? StatusFilter { get; set; }
        public int? MaxResults { get; set; }
    }
}