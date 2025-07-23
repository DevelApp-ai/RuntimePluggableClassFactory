using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Security
{
    /// <summary>
    /// Interface for validating plugin security before loading
    /// Implements TDS requirement for security hardening
    /// </summary>
    public interface IPluginSecurityValidator
    {
        /// <summary>
        /// Validates an assembly before loading it as a plugin
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly to validate</param>
        /// <returns>Validation result with security assessment</returns>
        Task<PluginSecurityValidationResult> ValidateAssemblyAsync(string assemblyPath);

        /// <summary>
        /// Validates a loaded assembly for security compliance
        /// </summary>
        /// <param name="assembly">The loaded assembly to validate</param>
        /// <returns>Validation result with security assessment</returns>
        PluginSecurityValidationResult ValidateLoadedAssembly(Assembly assembly);

        /// <summary>
        /// Validates plugin types for security compliance
        /// </summary>
        /// <param name="pluginTypes">Types to validate</param>
        /// <returns>Validation result with security assessment</returns>
        PluginSecurityValidationResult ValidatePluginTypes(IEnumerable<Type> pluginTypes);
    }

    /// <summary>
    /// Result of plugin security validation
    /// </summary>
    public class PluginSecurityValidationResult
    {
        /// <summary>
        /// Indicates if the plugin passed security validation
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Security risk level of the plugin
        /// </summary>
        public SecurityRiskLevel RiskLevel { get; set; }

        /// <summary>
        /// List of security issues found
        /// </summary>
        public List<SecurityIssue> Issues { get; set; } = new List<SecurityIssue>();

        /// <summary>
        /// List of security warnings
        /// </summary>
        public List<SecurityWarning> Warnings { get; set; } = new List<SecurityWarning>();

        /// <summary>
        /// Additional validation metadata
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a successful validation result
        /// </summary>
        /// <returns>Valid security result</returns>
        public static PluginSecurityValidationResult CreateValid()
        {
            return new PluginSecurityValidationResult
            {
                IsValid = true,
                RiskLevel = SecurityRiskLevel.Low
            };
        }

        /// <summary>
        /// Creates a failed validation result
        /// </summary>
        /// <param name="issues">Security issues found</param>
        /// <param name="riskLevel">Risk level</param>
        /// <returns>Invalid security result</returns>
        public static PluginSecurityValidationResult CreateInvalid(IEnumerable<SecurityIssue> issues, SecurityRiskLevel riskLevel = SecurityRiskLevel.High)
        {
            return new PluginSecurityValidationResult
            {
                IsValid = false,
                RiskLevel = riskLevel,
                Issues = new List<SecurityIssue>(issues)
            };
        }
    }

    /// <summary>
    /// Security risk levels for plugins
    /// </summary>
    public enum SecurityRiskLevel
    {
        /// <summary>
        /// Low risk - plugin appears safe
        /// </summary>
        Low,

        /// <summary>
        /// Medium risk - plugin has some concerning characteristics
        /// </summary>
        Medium,

        /// <summary>
        /// High risk - plugin has dangerous characteristics
        /// </summary>
        High,

        /// <summary>
        /// Critical risk - plugin should not be loaded
        /// </summary>
        Critical
    }

    /// <summary>
    /// Represents a security issue found in a plugin
    /// </summary>
    public class SecurityIssue
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public SecurityRiskLevel Severity { get; set; }
        public string Location { get; set; }
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Represents a security warning for a plugin
    /// </summary>
    public class SecurityWarning
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    }
}

