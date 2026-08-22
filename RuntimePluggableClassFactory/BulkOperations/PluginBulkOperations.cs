using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.BulkOperations
{
    /// <summary>
    /// Bulk operations for plugin factories
    /// Provides efficient batch operations for loading and executing multiple plugins
    /// </summary>
    public static class PluginBulkOperations
    {
        /// <summary>
        /// Bulk load result for multiple plugins
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        public class BulkLoadResult<T> where T : IPluginClass
        {
            /// <summary>
            /// Successfully loaded plugins
            /// </summary>
            public Dictionary<(NamespaceString ModuleName, IdentifierString Name), T> Successes { get; } = 
                new Dictionary<(NamespaceString, IdentifierString), T>();

            /// <summary>
            /// Failed plugin loads with error information
            /// </summary>
            public Dictionary<(NamespaceString ModuleName, IdentifierString Name), PluginLoadError> Failures { get; } = 
                new Dictionary<(NamespaceString, IdentifierString), PluginLoadError>();

            /// <summary>
            /// Total number of plugins attempted
            /// </summary>
            public int TotalAttempted => Successes.Count + Failures.Count;

            /// <summary>
            /// Number of successful loads
            /// </summary>
            public int SuccessCount => Successes.Count;

            /// <summary>
            /// Number of failed loads
            /// </summary>
            public int FailureCount => Failures.Count;

            /// <summary>
            /// Whether all plugins loaded successfully
            /// </summary>
            public bool AllSucceeded => Failures.Count == 0;

            /// <summary>
            /// Adds a successful load
            /// </summary>
            public void AddSuccess(NamespaceString moduleName, IdentifierString name, T instance)
            {
                Successes[(moduleName, name)] = instance;
            }

            /// <summary>
            /// Adds a failed load
            /// </summary>
            public void AddFailure(NamespaceString moduleName, IdentifierString name, PluginLoadError error)
            {
                Failures[(moduleName, name)] = error;
            }
        }

        /// <summary>
        /// Bulk execute result for multiple plugins
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TOutput">Output type</typeparam>
        public class BulkExecuteResult<T, TInput, TOutput> 
            where T : ITypedPluginClass<TInput, TOutput>
        {
            /// <summary>
            /// Successfully executed plugins with results
            /// </summary>
            public Dictionary<(NamespaceString ModuleName, IdentifierString Name), PluginExecutionResult<TOutput>> Successes { get; } = 
                new Dictionary<(NamespaceString, IdentifierString), PluginExecutionResult<TOutput>>();

            /// <summary>
            /// Failed plugin executions with error information
            /// </summary>
            public Dictionary<(NamespaceString ModuleName, IdentifierString Name), PluginExecutionError> Failures { get; } = 
                new Dictionary<(NamespaceString, IdentifierString), PluginExecutionError>();

            /// <summary>
            /// Total number of plugins executed
            /// </summary>
            public int TotalAttempted => Successes.Count + Failures.Count;

            /// <summary>
            /// Number of successful executions
            /// </summary>
            public int SuccessCount => Successes.Count;

            /// <summary>
            /// Number of failed executions
            /// </summary>
            public int FailureCount => Failures.Count;

            /// <summary>
            /// Whether all plugins executed successfully
            /// </summary>
            public bool AllSucceeded => Failures.Count == 0;

            /// <summary>
            /// Adds a successful execution
            /// </summary>
            public void AddSuccess(
                NamespaceString moduleName,
                IdentifierString name,
                PluginExecutionResult<TOutput> result)
            {
                Successes[(moduleName, name)] = result;
            }

            /// <summary>
            /// Adds a failed execution
            /// </summary>
            public void AddFailure(
                NamespaceString moduleName,
                IdentifierString name,
                PluginExecutionError error)
            {
                Failures[(moduleName, name)] = error;
            }
        }

        /// <summary>
        /// Error information for plugin load failures
        /// </summary>
        public class PluginLoadError
        {
            /// <summary>
            /// Error code
            /// </summary>
            public Errors.PluginErrorCode Code { get; set; }

            /// <summary>
            /// Error message
            /// </summary>
            public string? Message { get; set; }

            /// <summary>
            /// Exception
            /// </summary>
            public Exception? Exception { get; set; }

            /// <summary>
            /// Timestamp
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;

            public override string ToString()
            {
                return $"{(int)Code} {Code}: {Message} at {Timestamp:yyyy-MM-dd HH:mm:ss}"
            }
        }

        /// <summary>
        /// Error information for plugin execution failures
        /// </summary>
        public class PluginExecutionError
        {
            /// <summary>
            /// Error code
            /// </summary>
            public Errors.PluginErrorCode Code { get; set; }

            /// <summary>
            /// Error message
            /// </summary>
            public string? Message { get; set; }

            /// <summary>
            /// Exception
            /// </summary>
            public Exception? Exception { get; set; }

            /// <summary>
            /// Input that caused the error
            /// </summary>
            public object? Input { get; set; }

            /// <summary>
            /// Timestamp
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;

            public override string ToString()
            {
                return $"{(int)Code} {Code}: {Message} at {Timestamp:yyyy-MM-dd HH:mm:ss}"
            }
        }

        /// <summary>
        /// Loads multiple plugins in bulk
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="factory">Plugin factory</param>
        /// <param name="pluginIdentifiers">List of (module, name) pairs to load</param>
        /// <param name="parallel">Whether to load in parallel</param>
        /// <returns>Bulk load result</returns>
        public static BulkLoadResult<T> LoadPlugins<T>(
            PluginClassFactory<T> factory,
            IEnumerable<(NamespaceString ModuleName, IdentifierString Name)> pluginIdentifiers,
            bool parallel = true) where T : IPluginClass
        {
            var result = new BulkLoadResult<T>();

            if (parallel)
            {
                // Parallel loading
                var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                
                Parallel.ForEach(pluginIdentifiers, options, identifier =>
                {
                    try
                    {
                        var instance = factory.GetInstance(identifier.ModuleName, identifier.Name);
                        if (instance != null)
                        {
                            lock (result)
                            {
                                result.AddSuccess(identifier.ModuleName, identifier.Name, instance);
                            }
                        }
                        else
                        {
                            lock (result)
                            {
                                result.AddFailure(identifier.ModuleName, identifier.Name, 
                                    new PluginLoadError
                                    {
                                        Code = Errors.PluginErrorCode.Instantiation_Failed,
                                        Message = $"Plugin {identifier.ModuleName}.{identifier.Name} returned null"
                                    });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (result)
                        {
                            result.AddFailure(identifier.ModuleName, identifier.Name,
                                new PluginLoadError
                                {
                                    Code = Errors.PluginErrorCode.Instantiation_Failed,
                                    Message = $"Failed to load {identifier.ModuleName}.{identifier.Name}",
                                    Exception = ex
                                });
                        }
                    }
                });
            }
            else
            {
                // Sequential loading
                foreach (var identifier in pluginIdentifiers)
                {
                    try
                    {
                        var instance = factory.GetInstance(identifier.ModuleName, identifier.Name);
                        if (instance != null)
                        {
                            result.AddSuccess(identifier.ModuleName, identifier.Name, instance);
                        }
                        else
                        {
                            result.AddFailure(identifier.ModuleName, identifier.Name,
                                new PluginLoadError
                                {
                                    Code = Errors.PluginErrorCode.Instantiation_Failed,
                                    Message = $"Plugin {identifier.ModuleName}.{identifier.Name} returned null"
                                });
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddFailure(identifier.ModuleName, identifier.Name,
                            new PluginLoadError
                            {
                                Code = Errors.PluginErrorCode.Instantiation_Failed,
                                Message = $"Failed to load {identifier.ModuleName}.{identifier.Name}",
                                Exception = ex
                            });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Executes multiple plugins in bulk
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TOutput">Output type</typeparam>
        /// <param name="factory">Typed plugin factory</param>
        /// <param name="pluginIdentifiers">List of (module, name) pairs to execute</param>
        /// <param name="input">Input to pass to all plugins</param>
        /// <param name="parallel">Whether to execute in parallel</param>
        /// <returns>Bulk execute result</returns>
        public static async Task<BulkExecuteResult<T, TInput, TOutput>> ExecutePluginsAsync<T, TInput, TOutput>(
            TypedPluginClassFactory<T, TInput, TOutput> factory,
            IEnumerable<(NamespaceString ModuleName, IdentifierString Name)> pluginIdentifiers,
            TInput input,
            bool parallel = true) where T : ITypedPluginClass<TInput, TOutput>
        {
            var result = new BulkExecuteResult<T, TInput, TOutput>();

            if (parallel)
            {
                // Parallel execution
                var tasks = pluginIdentifiers.Select(async identifier =>
                {
                    try
                    {
                        var executionResult = await factory.ExecutePluginAsync(
                            identifier.ModuleName, 
                            identifier.Name, 
                            input);
                        
                        lock (result)
                        {
                            result.AddSuccess(identifier.ModuleName, identifier.Name, executionResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (result)
                        {
                            result.AddFailure(identifier.ModuleName, identifier.Name,
                                new PluginExecutionError
                                {
                                    Code = Errors.PluginErrorCode.Execution_Failed,
                                    Message = $"Failed to execute {identifier.ModuleName}.{identifier.Name}",
                                    Exception = ex,
                                    Input = input
                                });
                        }
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            else
            {
                // Sequential execution
                foreach (var identifier in pluginIdentifiers)
                {
                    try
                    {
                        var executionResult = await factory.ExecutePluginAsync(
                            identifier.ModuleName,
                            identifier.Name,
                            input).ConfigureAwait(false);
                        
                        result.AddSuccess(identifier.ModuleName, identifier.Name, executionResult);
                    }
                    catch (Exception ex)
                    {
                        result.AddFailure(identifier.ModuleName, identifier.Name,
                            new PluginExecutionError
                            {
                                Code = Errors.PluginErrorCode.Execution_Failed,
                                Message = $"Failed to execute {identifier.ModuleName}.{identifier.Name}",
                                Exception = ex,
                                Input = input
                            });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Loads and executes multiple plugins in a single operation
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TOutput">Output type</typeparam>
        /// <param name="factory">Typed plugin factory</param>
        /// <param name="pluginIdentifiers">List of (module, name) pairs</param>
        /// <param name="input">Input to pass to all plugins</param>
        /// <param name="parallel">Whether to execute in parallel</param>
        /// <returns>Combined result with load and execution information</returns>
        public static async Task<(BulkLoadResult<T> LoadResult, BulkExecuteResult<T, TInput, TOutput> ExecuteResult)> 
            LoadAndExecutePluginsAsync<T, TInput, TOutput>(
                TypedPluginClassFactory<T, TInput, TOutput> factory,
                IEnumerable<(NamespaceString ModuleName, IdentifierString Name)> pluginIdentifiers,
                TInput input,
                bool parallel = true) where T : ITypedPluginClass<TInput, TOutput>
        {
            var loadResult = LoadPlugins(factory, pluginIdentifiers, parallel);
            var executeResult = new BulkExecuteResult<T, TInput, TOutput>();

            // Execute only successfully loaded plugins
            foreach (var kvp in loadResult.Successes)
            {
                try
                {
                    var executionResult = await factory.ExecutePluginAsync(
                        kvp.Key.ModuleName,
                        kvp.Key.Name,
                        input).ConfigureAwait(false);
                    
                    executeResult.AddSuccess(kvp.Key.ModuleName, kvp.Key.Name, executionResult);
                }
                catch (Exception ex)
                {
                    executeResult.AddFailure(kvp.Key.ModuleName, kvp.Key.Name,
                        new PluginExecutionError
                        {
                            Code = Errors.PluginErrorCode.Execution_Failed,
                            Message = $"Failed to execute {kvp.Key.ModuleName}.{kvp.Key.Name}",
                            Exception = ex,
                            Input = input
                        });
                }
            }

            // Copy load failures to execute result
            foreach (var kvp in loadResult.Failures)
            {
                executeResult.AddFailure(kvp.Key.ModuleName, kvp.Key.Name,
                    new PluginExecutionError
                    {
                        Code = kvp.Value.Code,
                        Message = kvp.Value.Message,
                        Exception = kvp.Value.Exception
                    });
            }

            return (loadResult, executeResult);
        }
    }
}
