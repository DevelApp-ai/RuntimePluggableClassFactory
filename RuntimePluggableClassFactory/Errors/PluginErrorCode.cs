using System;

namespace DevelApp.RuntimePluggableClassFactory.Errors
{
    /// <summary>
    /// Structured error codes for plugin operations
    /// Provides consistent error identification across the plugin system
    /// </summary>
    public enum PluginErrorCode
    {
        #region Loading Errors (1000-1999)
        
        /// <summary>
        /// Plugin assembly not found
        /// </summary>
        Loading_AssemblyNotFound = 1000,

        /// <summary>
        /// Plugin assembly load failed
        /// </summary>
        Loading_AssemblyLoadFailed = 1001,

        /// <summary>
        /// Plugin type not found in assembly
        /// </summary>
        Loading_TypeNotFound = 1002,

        /// <summary>
        /// Plugin does not implement required interface
        /// </summary>
        Loading_InterfaceNotImplemented = 1003,

        /// <summary>
        /// Plugin directory not found
        /// </summary>
        Loading_DirectoryNotFound = 1004,

        /// <summary>
        /// No plugins found in directory
        /// </summary>
        Loading_NoPluginsFound = 1005,

        #endregion

        #region Security Errors (2000-2999)

        /// <summary>
        /// Security validation failed
        /// </summary>
        Security_ValidationFailed = 2000,

        /// <summary>
        /// Assembly size exceeds limit
        /// </summary>
        Security_AssemblyTooLarge = 2001,

        /// <summary>
        /// Invalid file extension
        /// </summary>
        Security_InvalidExtension = 2002,

        /// <summary>
        /// Digital signature verification failed
        /// </summary>
        Security_SignatureInvalid = 2003,

        /// <summary>
        /// Assembly contains prohibited types
        /// </summary>
        Security_ProhibitedType = 2004,

        /// <summary>
        /// Assembly contains dangerous methods
        /// </summary>
        Security_DangerousMethod = 2005,

        /// <summary>
        /// Assembly is from untrusted path
        /// </summary>
        Security_UntrustedPath = 2006,

        #endregion

        #region Instantiation Errors (3000-3999)

        /// <summary>
        /// Plugin instantiation failed
        /// </summary>
        Instantiation_Failed = 3000,

        /// <summary>
        /// No parameterless constructor found
        /// </summary>
        Instantiation_NoParameterlessConstructor = 3001,

        /// <summary>
        /// Constructor threw exception
        /// </summary>
        Instantiation_ConstructorException = 3002,

        /// <summary>
        /// Plugin is abstract
        /// </summary>
        Instantiation_AbstractType = 3003,

        /// <summary>
        /// Plugin is interface
        /// </summary>
        Instantiation_InterfaceType = 3004,

        #endregion

        #region Execution Errors (4000-4999)

        /// <summary>
        /// Plugin execution failed
        /// </summary>
        Execution_Failed = 4000,

        /// <summary>
        /// Plugin execution timed out
        /// </summary>
        Execution_Timeout = 4001,

        /// <summary>
        /// Plugin execution was cancelled
        /// </summary>
        Execution_Cancelled = 4002,

        /// <summary>
        /// Plugin returned null result
        /// </summary>
        Execution_NullResult = 4003,

        #endregion

        #region Unloading Errors (5000-5999)

        /// <summary>
        /// Plugin unloading failed
        /// </summary>
        Unloading_Failed = 5000,

        /// <summary>
        /// Assembly load context already unloaded
        /// </summary>
        Unloading_AlreadyUnloaded = 5001,

        /// <summary>
        /// Cannot unload while plugin is in use
        /// </summary>
        Unloading_InUse = 5002,

        #endregion

        #region Configuration Errors (6000-6999)

        /// <summary>
        /// Invalid plugin configuration
        /// </summary>
        Configuration_Invalid = 6000,

        /// <summary>
        /// Plugin version not found
        /// </summary>
        Configuration_VersionNotFound = 6001,

        /// <summary>
        /// Plugin not allowed
        /// </summary>
        Configuration_NotAllowed = 6002,

        /// <summary>
        /// Duplicate plugin registration
        /// </summary>
        Configuration_Duplicate = 6003,

        #endregion
    }

    /// <summary>
    /// Plugin error event arguments
    /// </summary>
    public class PluginErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Error code
        /// </summary>
        public PluginErrorCode Code { get; set; }

        /// <summary>
        /// Error message
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Exception that caused the error
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Module name
        /// </summary>
        public string? ModuleName { get; set; }

        /// <summary>
        /// Plugin name
        /// </summary>
        public string? PluginName { get; set; }

        /// <summary>
        /// Version
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Additional context
        /// </summary>
        public object? Context { get; set; }

        /// <summary>
        /// Creates a new plugin error event args
        /// </summary>
        public static PluginErrorEventArgs Create(
            PluginErrorCode code,
            string? message = null,
            Exception? exception = null,
            string? moduleName = null,
            string? pluginName = null,
            string? version = null,
            object? context = null)
        {
            return new PluginErrorEventArgs
            {
                Code = code,
                Message = message,
                Exception = exception,
                ModuleName = moduleName,
                PluginName = pluginName,
                Version = version,
                Context = context,
                Timestamp = DateTime.UtcNow
            };
        }

        public override string ToString()
        {
            return $[{(int)Code} {Code}] {Message} - {ModuleName}.{PluginName}@{Version} at {Timestamp:yyyy-MM-dd HH:mm:ss}];
        }
    }

    /// <summary>
    /// Exception for plugin errors with structured error codes
    /// </summary>
    public class PluginErrorException : Exception
    {
        /// <summary>
        /// Error code
        /// </summary>
        public PluginErrorCode Code { get; }

        /// <summary>
        /// Module name
        /// </summary>
        public string? ModuleName { get; }

        /// <summary>
        /// Plugin name
        /// </summary>
        public string? PluginName { get; }

        /// <summary>
        /// Version
        /// </summary>
        public string? Version { get; }

        /// <summary>
        /// Creates a new plugin error exception
        /// </summary>
        public PluginErrorException(
            PluginErrorCode code,
            string? message = null,
            Exception? innerException = null,
            string? moduleName = null,
            string? pluginName = null,
            string? version = null)
            : base(message ?? code.ToString(), innerException)
        {
            Code = code;
            ModuleName = moduleName;
            PluginName = pluginName;
            Version = version;
        }

        public override string ToString()
        {
            return $[{(int)Code} {Code}] {Message} - {ModuleName}.{PluginName}@{Version}];
        }
    }
}
