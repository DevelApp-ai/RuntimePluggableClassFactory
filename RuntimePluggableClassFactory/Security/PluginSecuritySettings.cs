using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DevelApp.RuntimePluggableClassFactory.Security
{
    /// <summary>
    /// Configuration settings for plugin security validation
    /// Implements TDS requirement for configurable security policies
    /// </summary>
    public class PluginSecuritySettings
    {
        /// <summary>
        /// Whether to require digital signatures on plugin assemblies
        /// </summary>
        public bool RequireDigitalSignature { get; set; } = false;

        /// <summary>
        /// Maximum allowed size for plugin assemblies in bytes
        /// </summary>
        public long MaxAssemblySizeBytes { get; set; } = 50 * 1024 * 1024; // 50 MB

        /// <summary>
        /// Allowed file extensions for plugin assemblies
        /// </summary>
        public HashSet<string> AllowedExtensions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe"
        };

        /// <summary>
        /// Trusted paths from which plugins can be loaded
        /// </summary>
        public List<string> TrustedPaths { get; set; } = new List<string>();

        /// <summary>
        /// Base types that plugins are prohibited from inheriting
        /// </summary>
        public List<Type> ProhibitedBaseTypes { get; set; } = new List<Type>
        {
            typeof(Process),
            typeof(AppDomain)
        };

        /// <summary>
        /// Namespaces that plugins are prohibited from using
        /// </summary>
        public List<string> ProhibitedNamespaces { get; set; } = new List<string>
        {
            "System.Diagnostics",
            "System.IO.Pipes",
            "System.Net.Sockets",
            "System.Runtime.InteropServices",
            "System.Security.Cryptography",
            "Microsoft.Win32"
        };

        /// <summary>
        /// Field types that are prohibited in plugins
        /// </summary>
        public List<Type> ProhibitedFieldTypes { get; set; } = new List<Type>
        {
            typeof(Process),
            typeof(FileStream),
            typeof(NetworkStream)
        };

        /// <summary>
        /// Whether to allow plugins to access the file system
        /// </summary>
        public bool AllowFileSystemAccess { get; set; } = false;

        /// <summary>
        /// Whether to allow plugins to access the network
        /// </summary>
        public bool AllowNetworkAccess { get; set; } = false;

        /// <summary>
        /// Whether to allow plugins to use reflection
        /// </summary>
        public bool AllowReflection { get; set; } = true;

        /// <summary>
        /// Maximum execution time for plugin operations in milliseconds
        /// </summary>
        public int MaxExecutionTimeMs { get; set; } = 30000; // 30 seconds

        /// <summary>
        /// Maximum memory usage for plugin operations in bytes
        /// </summary>
        public long MaxMemoryUsageBytes { get; set; } = 100 * 1024 * 1024; // 100 MB

        /// <summary>
        /// Creates default security settings with moderate restrictions
        /// </summary>
        /// <returns>Default security settings</returns>
        public static PluginSecuritySettings CreateDefault()
        {
            return new PluginSecuritySettings();
        }

        /// <summary>
        /// Creates strict security settings with high restrictions
        /// </summary>
        /// <returns>Strict security settings</returns>
        public static PluginSecuritySettings CreateStrict()
        {
            var settings = new PluginSecuritySettings
            {
                RequireDigitalSignature = true,
                MaxAssemblySizeBytes = 10 * 1024 * 1024, // 10 MB
                AllowFileSystemAccess = false,
                AllowNetworkAccess = false,
                AllowReflection = false,
                MaxExecutionTimeMs = 10000, // 10 seconds
                MaxMemoryUsageBytes = 50 * 1024 * 1024 // 50 MB
            };

            // Add more prohibited namespaces for strict mode
            settings.ProhibitedNamespaces.AddRange(new[]
            {
                "System.IO",
                "System.Net",
                "System.Threading",
                "System.Reflection.Emit",
                "System.Runtime.Loader"
            });

            return settings;
        }

        /// <summary>
        /// Creates permissive security settings with minimal restrictions
        /// </summary>
        /// <returns>Permissive security settings</returns>
        public static PluginSecuritySettings CreatePermissive()
        {
            return new PluginSecuritySettings
            {
                RequireDigitalSignature = false,
                MaxAssemblySizeBytes = 200 * 1024 * 1024, // 200 MB
                AllowFileSystemAccess = true,
                AllowNetworkAccess = true,
                AllowReflection = true,
                MaxExecutionTimeMs = 120000, // 2 minutes
                MaxMemoryUsageBytes = 500 * 1024 * 1024, // 500 MB
                ProhibitedNamespaces = new List<string>(), // No prohibited namespaces
                ProhibitedBaseTypes = new List<Type>(), // No prohibited base types
                ProhibitedFieldTypes = new List<Type>() // No prohibited field types
            };
        }

        /// <summary>
        /// Validates the security settings for consistency
        /// </summary>
        /// <returns>True if settings are valid, false otherwise</returns>
        public bool ValidateSettings()
        {
            if (MaxAssemblySizeBytes <= 0)
                return false;

            if (MaxExecutionTimeMs <= 0)
                return false;

            if (MaxMemoryUsageBytes <= 0)
                return false;

            if (AllowedExtensions == null || AllowedExtensions.Count == 0)
                return false;

            return true;
        }

        /// <summary>
        /// Adds a trusted path for plugin loading
        /// </summary>
        /// <param name="path">Path to add as trusted</param>
        public void AddTrustedPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                var fullPath = Path.GetFullPath(path);
                if (!TrustedPaths.Contains(fullPath))
                {
                    TrustedPaths.Add(fullPath);
                }
            }
        }

        /// <summary>
        /// Removes a trusted path
        /// </summary>
        /// <param name="path">Path to remove from trusted paths</param>
        public void RemoveTrustedPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var fullPath = Path.GetFullPath(path);
                TrustedPaths.Remove(fullPath);
            }
        }
    }
}

