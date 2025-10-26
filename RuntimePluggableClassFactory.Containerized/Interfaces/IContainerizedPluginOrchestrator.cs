using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces
{
    /// <summary>
    /// Main interface for the Containerized RuntimePluggableClassFactory (CRPCF)
    /// Coordinates plugin deployment, execution, and lifecycle management
    /// </summary>
    public interface IContainerizedPluginOrchestrator
    {
        /// <summary>
        /// Deploys a signed NuGet package as a containerized plugin
        /// </summary>
        /// <param name="request">Deployment request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Deployment result</returns>
        Task<PluginDeploymentResult> DeployPluginAsync(
            PluginDeploymentRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a containerized plugin
        /// </summary>
        /// <param name="request">Execution request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Execution result</returns>
        Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(
            ContainerizedPluginExecutionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about a deployed plugin
        /// </summary>
        /// <param name="pluginId">Plugin identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Plugin information</returns>
        Task<ContainerizedPluginInfo?> GetPluginInfoAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists all deployed plugins
        /// </summary>
        /// <param name="options">List options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Plugin information list</returns>
        Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(
            PluginListOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Undeploys a containerized plugin
        /// </summary>
        /// <param name="pluginId">Plugin identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task UndeployPluginAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets health status of a deployed plugin
        /// </summary>
        /// <param name="pluginId">Plugin identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Plugin health information</returns>
        Task<PluginHealth> GetPluginHealthAsync(
            PluginIdentifier pluginId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Notifies about a plugin update
        /// </summary>
        /// <param name="pluginId">Plugin identifier</param>
        /// <param name="newVersion">New version</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task NotifyPluginUpdateAsync(
            PluginIdentifier pluginId,
            SemanticVersionNumber newVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Raised when a plugin is deployed
        /// </summary>
        event EventHandler<PluginEventArgs>? PluginDeployed;

        /// <summary>
        /// Raised when a plugin is executed
        /// </summary>
        event EventHandler<PluginEventArgs>? PluginExecuted;

        /// <summary>
        /// Raised when a plugin operation fails
        /// </summary>
        event EventHandler<PluginEventArgs>? PluginFailed;

        /// <summary>
        /// Raised when a plugin is undeployed
        /// </summary>
        event EventHandler<PluginEventArgs>? PluginUndeployed;
    }

    /// <summary>
    /// Plugin deployment request
    /// </summary>
    public class PluginDeploymentRequest
    {
        public Stream PackageStream { get; set; } = Stream.Null;
        public string TargetPlatform { get; set; } = string.Empty;
        public DeploymentConfiguration Configuration { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Deployment configuration
    /// </summary>
    public class DeploymentConfiguration
    {
        public int MemoryLimitMB { get; set; } = 512;
        public double CpuLimit { get; set; } = 0.5;
        public int DiskLimitMB { get; set; } = 1024;
        public bool ReadOnlyFileSystem { get; set; } = true;
        public NetworkPolicy NetworkPolicy { get; set; } = NetworkPolicy.Isolated;
        public Dictionary<string, string>? EnvironmentVariables { get; set; }
        public int ExecutionTimeoutMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Plugin deployment result
    /// </summary>
    public class PluginDeploymentResult
    {
        public bool Success { get; set; }
        public string DeploymentId { get; set; } = string.Empty;
        public PluginIdentifier? PluginId { get; set; }
        public ContainerInfo? ContainerInfo { get; set; }
        public IEnumerable<string> Errors { get; set; } = Array.Empty<string>();
        public IEnumerable<string> Warnings { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Container information
    /// </summary>
    public class ContainerInfo
    {
        public string Platform { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    /// <summary>
    /// Containerized plugin execution request
    /// </summary>
    public class ContainerizedPluginExecutionRequest
    {
        public PluginIdentifier PluginId { get; set; } = new();
        public string InputData { get; set; } = string.Empty;
        public Dictionary<string, object> Configuration { get; set; } = new();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Plugin identifier
    /// </summary>
    public class PluginIdentifier
    {
        public NamespaceString Namespace { get; set; } = new();
        public IdentifierString Name { get; set; } = new();
        public SemanticVersionNumber Version { get; set; } = new(1, 0, 0);
        
        public override string ToString() => $"{Namespace}.{Name}@{Version}";

        public override bool Equals(object? obj)
        {
            if (obj is PluginIdentifier other)
            {
                return Namespace.Equals(other.Namespace) &&
                       Name.Equals(other.Name) &&
                       Version.Equals(other.Version);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Namespace, Name, Version);
        }
    }

    /// <summary>
    /// Containerized plugin information
    /// </summary>
    public class ContainerizedPluginInfo
    {
        public PluginIdentifier PluginId { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public DateTime DeployedAt { get; set; }
        public PluginDeploymentStatus Status { get; set; }
        public ContainerInfo? ContainerInfo { get; set; }
        public SignerInfo? SignerInfo { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Plugin deployment status
    /// </summary>
    public enum PluginDeploymentStatus
    {
        Deploying,
        Deployed,
        Running,
        Stopped,
        Failed,
        Undeploying
    }

    /// <summary>
    /// Plugin list options
    /// </summary>
    public class PluginListOptions
    {
        public PluginDeploymentStatus? StatusFilter { get; set; }
        public string? NamespaceFilter { get; set; }
        public Dictionary<string, string>? LabelSelector { get; set; }
        public int? MaxResults { get; set; }
    }

    /// <summary>
    /// Plugin health information
    /// </summary>
    public class PluginHealth
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; }
        public IEnumerable<HealthCheck> Checks { get; set; } = Array.Empty<HealthCheck>();
    }

    /// <summary>
    /// Individual health check
    /// </summary>
    public class HealthCheck
    {
        public string Name { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Containerized plugin execution result
    /// </summary>
    public class ContainerizedPluginExecutionResult
    {
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string ExecutionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Plugin event arguments
    /// </summary>
    public class PluginEventArgs : EventArgs
    {
        public PluginIdentifier? PluginId { get; set; }
        public string DeploymentId { get; set; } = string.Empty;
        public string ExecutionId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}