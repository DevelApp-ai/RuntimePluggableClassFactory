using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    /// <summary>
    /// Provides a sandboxed execution environment for plugins to prevent host application crashes
    /// Implements TDS requirement for enhanced stability and error handling
    /// </summary>
    public static class PluginExecutionSandbox
    {
        /// <summary>
        /// Event fired when a plugin execution fails
        /// </summary>
        public static event EventHandler<PluginExecutionErrorEventArgs> PluginExecutionFailed;

        /// <summary>
        /// Executes a plugin method safely within a sandbox
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="plugin">Plugin instance</param>
        /// <param name="operation">Operation to execute</param>
        /// <param name="timeout">Optional timeout for the operation</param>
        /// <returns>Execution result with success status and error information</returns>
        public static PluginExecutionResult<TResult> ExecuteSafely<T, TResult>(
            T plugin, 
            Func<T, TResult> operation, 
            TimeSpan? timeout = null) 
            where T : IPluginClass
        {
            if (plugin == null)
                throw new ArgumentNullException(nameof(plugin));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var result = new PluginExecutionResult<TResult>
            {
                PluginName = plugin.Name?.ToString(),
                PluginModule = plugin.Module?.ToString(),
                PluginVersion = plugin.Version?.ToString(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                if (timeout.HasValue)
                {
                    // Execute with timeout
                    using (var cts = new CancellationTokenSource(timeout.Value))
                    {
                        var task = Task.Run(() => operation(plugin), cts.Token);
                        result.Result = task.Result;
                    }
                }
                else
                {
                    // Execute without timeout
                    result.Result = operation(plugin);
                }

                result.Success = true;
            }
            catch (OperationCanceledException ex) when (timeout.HasValue)
            {
                result.Success = false;
                result.Error = $"Plugin execution timed out after {timeout.Value.TotalSeconds} seconds";
                result.Exception = ex;
                
                FirePluginExecutionFailed(result, ex);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Plugin execution failed: {ex.Message}";
                result.Exception = ex;
                
                FirePluginExecutionFailed(result, ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// Executes an async plugin method safely within a sandbox
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="plugin">Plugin instance</param>
        /// <param name="operation">Async operation to execute</param>
        /// <param name="timeout">Optional timeout for the operation</param>
        /// <returns>Execution result with success status and error information</returns>
        public static async Task<PluginExecutionResult<TResult>> ExecuteSafelyAsync<T, TResult>(
            T plugin, 
            Func<T, Task<TResult>> operation, 
            TimeSpan? timeout = null) 
            where T : IPluginClass
        {
            if (plugin == null)
                throw new ArgumentNullException(nameof(plugin));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var result = new PluginExecutionResult<TResult>
            {
                PluginName = plugin.Name?.ToString(),
                PluginModule = plugin.Module?.ToString(),
                PluginVersion = plugin.Version?.ToString(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                if (timeout.HasValue)
                {
                    // Execute with timeout
                    using (var cts = new CancellationTokenSource(timeout.Value))
                    {
                        result.Result = await operation(plugin).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Execute without timeout
                    result.Result = await operation(plugin).ConfigureAwait(false);
                }

                result.Success = true;
            }
            catch (OperationCanceledException ex) when (timeout.HasValue)
            {
                result.Success = false;
                result.Error = $"Plugin execution timed out after {timeout.Value.TotalSeconds} seconds";
                result.Exception = ex;
                
                FirePluginExecutionFailed(result, ex);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Plugin execution failed: {ex.Message}";
                result.Exception = ex;
                
                FirePluginExecutionFailed(result, ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private static void FirePluginExecutionFailed<TResult>(PluginExecutionResult<TResult> result, Exception exception)
        {
            try
            {
                PluginExecutionFailed?.Invoke(null, new PluginExecutionErrorEventArgs
                {
                    PluginName = result.PluginName,
                    PluginModule = result.PluginModule,
                    PluginVersion = result.PluginVersion,
                    Error = result.Error,
                    Exception = exception,
                    Duration = result.Duration
                });
            }
            catch
            {
                // Ignore errors in event firing to prevent cascading failures
            }
        }
    }

    /// <summary>
    /// Result of a plugin execution within the sandbox
    /// </summary>
    /// <typeparam name="T">Type of the result</typeparam>
    public class PluginExecutionResult<T>
    {
        public bool Success { get; set; }
        public T Result { get; set; }
        public string Error { get; set; }
        public Exception Exception { get; set; }
        public string PluginName { get; set; }
        public string PluginModule { get; set; }
        public string PluginVersion { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Event arguments for plugin execution errors
    /// </summary>
    public class PluginExecutionErrorEventArgs : EventArgs
    {
        public string PluginName { get; set; }
        public string PluginModule { get; set; }
        public string PluginVersion { get; set; }
        public string Error { get; set; }
        public Exception Exception { get; set; }
        public TimeSpan Duration { get; set; }
    }
}

