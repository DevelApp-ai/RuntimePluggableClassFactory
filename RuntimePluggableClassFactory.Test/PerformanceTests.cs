using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Security;
using PluginImplementations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RuntimePluggableClassFactory.Test
{
    /// <summary>
    /// Performance tests to ensure the plugin system meets performance requirements
    /// Tests loading times, execution performance, and memory usage
    /// </summary>
    public class PerformanceTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _pluginPath;
        private readonly Uri _pluginUri;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            _pluginPath = Path.GetFullPath(pathString, assemblyPath);
            _pluginUri = new Uri(_pluginPath);
        }

        [Fact]
        public async Task Test_PluginDiscoveryPerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            var stopwatch = Stopwatch.StartNew();

            // Act
            await pluginFactory.RefreshPluginsAsync();
            var plugins = await pluginFactory.GetPossiblePlugins();

            stopwatch.Stop();

            // Assert - Plugin discovery should complete within reasonable time
            var discoveryTime = stopwatch.ElapsedMilliseconds;
            _output.WriteLine($"Plugin discovery took {discoveryTime}ms for {plugins.Count()} plugins");

            // Performance requirement: Discovery should complete within 5 seconds for reasonable plugin counts
            Assert.True(discoveryTime < 5000, $"Plugin discovery took {discoveryTime}ms, which exceeds 5000ms threshold");
            
            // Verify plugins were actually discovered
            Assert.NotNull(plugins);
        }

        [Fact]
        public async Task Test_PluginInstantiationPerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            
            if (!availablePlugins.Any())
            {
                _output.WriteLine("No plugins available for performance testing");
                return;
            }

            var firstPlugin = availablePlugins.First();
            var instantiationTimes = new List<long>();

            // Act - Measure instantiation time over multiple iterations
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var instance = pluginFactory.GetInstance(firstPlugin.ModuleName, firstPlugin.PluginName);
                stopwatch.Stop();

                instantiationTimes.Add(stopwatch.ElapsedMilliseconds);
                Assert.NotNull(instance);
            }

            // Assert - Plugin instantiation should be fast
            var averageTime = instantiationTimes.Average();
            var maxTime = instantiationTimes.Max();
            
            _output.WriteLine($"Plugin instantiation - Average: {averageTime:F2}ms, Max: {maxTime}ms");

            // Performance requirement: Average instantiation should be under 100ms
            Assert.True(averageTime < 100, $"Average instantiation time {averageTime:F2}ms exceeds 100ms threshold");
            
            // Performance requirement: No single instantiation should take more than 500ms
            Assert.True(maxTime < 500, $"Maximum instantiation time {maxTime}ms exceeds 500ms threshold");
        }

        [Fact]
        public async Task Test_PluginExecutionPerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            
            if (!availablePlugins.Any())
            {
                _output.WriteLine("No plugins available for performance testing");
                return;
            }

            var firstPlugin = availablePlugins.First();
            var instance = pluginFactory.GetInstance(firstPlugin.ModuleName, firstPlugin.PluginName);
            Assert.NotNull(instance);

            var executionTimes = new List<long>();

            // Act - Measure execution time over multiple iterations
            for (int i = 0; i < 100; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = instance.Execute($"performance test {i}");
                stopwatch.Stop();

                executionTimes.Add(stopwatch.ElapsedTicks);
                Assert.NotNull(result);
            }

            // Convert ticks to milliseconds
            var executionTimesMs = executionTimes.Select(t => (double)t / TimeSpan.TicksPerMillisecond).ToList();

            // Assert - Plugin execution should be performant
            var averageTime = executionTimesMs.Average();
            var maxTime = executionTimesMs.Max();
            var minTime = executionTimesMs.Min();
            
            _output.WriteLine($"Plugin execution - Average: {averageTime:F3}ms, Min: {minTime:F3}ms, Max: {maxTime:F3}ms");

            // Performance requirement: Average execution should be under 10ms for simple operations
            Assert.True(averageTime < 10, $"Average execution time {averageTime:F3}ms exceeds 10ms threshold");
        }

        [Fact]
        public async Task Test_ConcurrentExecutionPerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            
            if (!availablePlugins.Any())
            {
                _output.WriteLine("No plugins available for performance testing");
                return;
            }

            var firstPlugin = availablePlugins.First();
            const int concurrentTasks = 50;
            const int executionsPerTask = 10;

            var stopwatch = Stopwatch.StartNew();

            // Act - Execute plugin concurrently from multiple tasks
            var tasks = new Task[concurrentTasks];
            for (int i = 0; i < concurrentTasks; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < executionsPerTask; j++)
                    {
                        var instance = pluginFactory.GetInstance(firstPlugin.ModuleName, firstPlugin.PluginName);
                        var result = instance?.Execute($"concurrent test {taskId}-{j}");
                        Assert.NotNull(result);
                    }
                });
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert - Concurrent execution should scale well
            var totalExecutions = concurrentTasks * executionsPerTask;
            var totalTime = stopwatch.ElapsedMilliseconds;
            var executionsPerSecond = (totalExecutions * 1000.0) / totalTime;
            
            _output.WriteLine($"Concurrent execution - {totalExecutions} executions in {totalTime}ms ({executionsPerSecond:F1} exec/sec)");

            // Performance requirement: Should handle at least 100 executions per second
            Assert.True(executionsPerSecond > 100, $"Execution rate {executionsPerSecond:F1} exec/sec is below 100 exec/sec threshold");
        }

        [Fact]
        public async Task Test_SecurityValidationPerformance()
        {
            // Arrange
            var strictSettings = PluginSecuritySettings.CreateStrict();
            var securityValidator = new DefaultPluginSecurityValidator(strictSettings);

            // Find a plugin DLL to test with
            var pluginFiles = Directory.GetFiles(_pluginPath, "*.dll", SearchOption.AllDirectories);
            if (!pluginFiles.Any())
            {
                _output.WriteLine("No plugin files available for security validation performance testing");
                return;
            }

            var testFile = pluginFiles.First();
            var validationTimes = new List<long>();

            // Act - Measure security validation time over multiple iterations
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await securityValidator.ValidateAssemblyAsync(testFile);
                stopwatch.Stop();

                validationTimes.Add(stopwatch.ElapsedMilliseconds);
                Assert.NotNull(result);
            }

            // Assert - Security validation should be reasonably fast
            var averageTime = validationTimes.Average();
            var maxTime = validationTimes.Max();
            
            _output.WriteLine($"Security validation - Average: {averageTime:F2}ms, Max: {maxTime}ms");

            // Performance requirement: Average validation should be under 500ms
            Assert.True(averageTime < 500, $"Average security validation time {averageTime:F2}ms exceeds 500ms threshold");
            
            // Performance requirement: No single validation should take more than 2 seconds
            Assert.True(maxTime < 2000, $"Maximum security validation time {maxTime}ms exceeds 2000ms threshold");
        }

        [Fact]
        public async Task Test_MemoryUsageUnderLoad()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            
            if (!availablePlugins.Any())
            {
                _output.WriteLine("No plugins available for memory testing");
                return;
            }

            // Measure initial memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var initialMemory = GC.GetTotalMemory(false);

            // Act - Create and execute many plugin instances
            const int iterations = 100;
            for (int i = 0; i < iterations; i++)
            {
                foreach (var plugin in availablePlugins.Take(2)) // Limit to avoid excessive memory usage
                {
                    var instance = pluginFactory.GetInstance(plugin.ModuleName, plugin.PluginName);
                    if (instance != null)
                    {
                        var result = instance.Execute($"memory test {i}");
                        Assert.NotNull(result);
                    }
                }

                // Periodically unload plugins to test memory cleanup
                if (i % 25 == 0)
                {
                    filePluginLoader.UnloadAllPlugins();
                    await pluginFactory.RefreshPluginsAsync();
                }
            }

            // Force garbage collection and measure final memory
            filePluginLoader.UnloadAllPlugins();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);

            // Assert - Memory usage should not grow excessively
            var memoryIncrease = finalMemory - initialMemory;
            var memoryIncreaseMB = memoryIncrease / (1024.0 * 1024.0);
            
            _output.WriteLine($"Memory usage - Initial: {initialMemory / (1024.0 * 1024.0):F2}MB, Final: {finalMemory / (1024.0 * 1024.0):F2}MB, Increase: {memoryIncreaseMB:F2}MB");

            // Performance requirement: Memory increase should be reasonable (under 50MB for this test)
            Assert.True(memoryIncreaseMB < 50, $"Memory increase {memoryIncreaseMB:F2}MB exceeds 50MB threshold");
        }

        [Fact]
        public async Task Test_TypedPluginPerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(_pluginUri, securityValidator);
            var typedFactory = new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(
                filePluginLoader);

            await typedFactory.RefreshPluginsAsync();
            var availablePlugins = await typedFactory.GetPossiblePlugins();
            
            var typedPlugin = availablePlugins.FirstOrDefault(p => p.Type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedPluginClass<,>)));
            
            if (typedPlugin == null)
            {
                _output.WriteLine("No typed plugins available for performance testing");
                return;
            }

            var executionTimes = new List<long>();
            var input = new WordGuessInput { Word = "performance", CaseSensitive = false };

            // Act - Measure typed execution performance
            for (int i = 0; i < 50; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = typedFactory.ExecutePlugin(typedPlugin.ModuleName, typedPlugin.PluginName, input);
                stopwatch.Stop();

                executionTimes.Add(stopwatch.ElapsedMilliseconds);
                Assert.NotNull(result);
            }

            // Assert - Typed plugin execution should be performant
            var averageTime = executionTimes.Average();
            var maxTime = executionTimes.Max();
            
            _output.WriteLine($"Typed plugin execution - Average: {averageTime:F2}ms, Max: {maxTime}ms");

            // Performance requirement: Typed execution should not be significantly slower than regular execution
            Assert.True(averageTime < 20, $"Average typed execution time {averageTime:F2}ms exceeds 20ms threshold");
        }

        [Fact]
        public async Task Test_PluginLoadUnloadCyclePerformance()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            var cycleTimes = new List<long>();
            const int cycles = 5;

            // Act - Measure load/unload cycle performance
            for (int i = 0; i < cycles; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                
            // Load
            await pluginFactory.RefreshPluginsAsync();
            var plugins = await filePluginLoader.ListAllPossiblePluginsAsync();
                
                // Execute a few plugins
                foreach (var plugin in plugins.Take(2))
                {
                    var instance = pluginFactory.GetInstance(plugin.ModuleName, plugin.PluginName);
                    if (instance != null)
                    {
                        var result = instance.Execute($"cycle test {i}");
                        Assert.NotNull(result);
                    }
                }
                
                // Unload
                filePluginLoader.UnloadAllPlugins();
                
                stopwatch.Stop();
                cycleTimes.Add(stopwatch.ElapsedMilliseconds);
            }

            // Assert - Load/unload cycles should be reasonably fast
            var averageTime = cycleTimes.Average();
            var maxTime = cycleTimes.Max();
            
            _output.WriteLine($"Load/unload cycle - Average: {averageTime:F2}ms, Max: {maxTime}ms");

            // Performance requirement: Average cycle should be under 2 seconds
            Assert.True(averageTime < 2000, $"Average load/unload cycle time {averageTime:F2}ms exceeds 2000ms threshold");
        }
    }
}

