using System;
using System.Collections.Generic;
using System.Threading;

namespace DevelApp.RuntimePluggableClassFactory.Interface
{
    /// <summary>
    /// Generic typed plugin interface providing type safety for input and output data
    /// Implements TDS requirement for strongly-typed DTOs
    /// </summary>
    /// <typeparam name="TInput">Type of input data for the plugin</typeparam>
    /// <typeparam name="TOutput">Type of output data from the plugin</typeparam>
    public interface ITypedPluginClass<TInput, TOutput> : IPluginClass
    {
        /// <summary>
        /// Executes the plugin with strongly-typed input and output
        /// </summary>
        /// <param name="context">Execution context providing logging and cancellation support</param>
        /// <param name="input">Strongly-typed input data</param>
        /// <returns>Execution result with success status, output data, and error information</returns>
        PluginExecutionResult<TOutput> Execute(IPluginExecutionContext context, TInput input);
    }

    /// <summary>
    /// Execution context providing infrastructure services to plugins
    /// Implements TDS requirement for functional context with logging and cancellation
    /// </summary>
    public interface IPluginExecutionContext
    {
        /// <summary>
        /// Logger instance for plugin logging
        /// </summary>
        IPluginLogger Logger { get; }

        /// <summary>
        /// Cancellation token for cooperative cancellation
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Additional properties that can be passed to plugins
        /// </summary>
        IReadOnlyDictionary<string, object> Properties { get; }
    }

    /// <summary>
    /// Simple logger interface for plugins to avoid external dependencies
    /// </summary>
    public interface IPluginLogger
    {
        /// <summary>
        /// Logs an informational message
        /// </summary>
        /// <param name="message">Message to log</param>
        void LogInformation(string message);

        /// <summary>
        /// Logs a warning message
        /// </summary>
        /// <param name="message">Message to log</param>
        void LogWarning(string message);

        /// <summary>
        /// Logs an error message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void LogError(string message, Exception exception = null);

        /// <summary>
        /// Logs a debug message
        /// </summary>
        /// <param name="message">Message to log</param>
        void LogDebug(string message);
    }

    /// <summary>
    /// Result of a typed plugin execution
    /// </summary>
    /// <typeparam name="T">Type of the output data</typeparam>
    public class PluginExecutionResult<T>
    {
        /// <summary>
        /// Indicates if the execution was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The output data from the plugin (only valid if Success is true)
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// Error message if execution failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Exception that caused the failure (if any)
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Creates a successful result
        /// </summary>
        /// <param name="data">The output data</param>
        /// <returns>Successful execution result</returns>
        public static PluginExecutionResult<T> CreateSuccess(T data)
        {
            return new PluginExecutionResult<T>
            {
                Success = true,
                Data = data
            };
        }

        /// <summary>
        /// Creates a failed result
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <param name="exception">Optional exception</param>
        /// <returns>Failed execution result</returns>
        public static PluginExecutionResult<T> CreateFailure(string errorMessage, Exception exception = null)
        {
            return new PluginExecutionResult<T>
            {
                Success = false,
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }
    }
}

