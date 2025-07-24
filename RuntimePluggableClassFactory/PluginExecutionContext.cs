using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.Collections.Generic;
using System.Threading;

namespace DevelApp.RuntimePluggableClassFactory
{
    /// <summary>
    /// Default implementation of IPluginExecutionContext
    /// </summary>
    public class PluginExecutionContext : IPluginExecutionContext
    {
        public IPluginLogger Logger { get; }
        public CancellationToken CancellationToken { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        public PluginExecutionContext(
            IPluginLogger logger = null, 
            CancellationToken cancellationToken = default, 
            IReadOnlyDictionary<string, object> properties = null)
        {
            Logger = logger ?? new ConsolePluginLogger();
            CancellationToken = cancellationToken;
            Properties = properties ?? new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Simple console-based logger implementation
    /// </summary>
    public class ConsolePluginLogger : IPluginLogger
    {
        public void LogInformation(string message)
        {
            Console.WriteLine($"[INFO] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"[WARN] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogError(string message, Exception exception = null)
        {
            Console.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
            if (exception != null)
            {
                Console.WriteLine($"[ERROR] Exception: {exception}");
            }
        }

        public void LogDebug(string message)
        {
            Console.WriteLine($"[DEBUG] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        }
    }

    /// <summary>
    /// No-operation logger that discards all log messages
    /// </summary>
    public class NullPluginLogger : IPluginLogger
    {
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception exception = null) { }
        public void LogDebug(string message) { }
    }
}

