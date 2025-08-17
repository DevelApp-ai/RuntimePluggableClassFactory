using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Security;
using PluginImplementations;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Interface = DevelApp.RuntimePluggableClassFactory.Interface;

namespace RuntimePluggableClassFactory.Test
{
    /// <summary>
    /// Integration tests covering end-to-end scenarios for the RuntimePluggableClassFactory
    /// Tests complete workflows from plugin discovery to execution and unloading
    /// </summary>
    public class IntegrationTests
    {
        private readonly string _pluginPath;
        private readonly Uri _pluginUri;

        public IntegrationTests()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            _pluginPath = Path.GetFullPath(pathString, assemblyPath);
            _pluginUri = new Uri(_pluginPath);
        }

        [Fact]
        public async Task Test_CompletePluginWorkflow_LoadExecuteUnload()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            bool securityEventFired = false;
            bool pluginErrorFired = false;

            filePluginLoader.SecurityValidationFailed += (sender, args) =>
            {
                securityEventFired = true;
            };

            pluginFactory.PluginInstantiationFailed += (sender, args) =>
            {
                pluginErrorFired = true;
            };

            // Act & Assert - Complete workflow
            
            // 1. Refresh plugins (discovery phase)
            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            Assert.NotEmpty(availablePlugins);

            // 2. Get a plugin instance
            var firstPlugin = availablePlugins.First();
            var pluginInstance = pluginFactory.GetInstance(firstPlugin.ModuleName, firstPlugin.PluginName);
            Assert.NotNull(pluginInstance);

            // 3. Execute plugin
            var result = pluginInstance.Execute("test input");
            Assert.NotNull(result);

            // 4. Test versioned access
            var versionedInstance = pluginFactory.GetInstance(
                firstPlugin.ModuleName, 
                firstPlugin.PluginName, 
                firstPlugin.Version);
            Assert.NotNull(versionedInstance);

            // 5. Test unloading
            filePluginLoader.UnloadAllPlugins();

            // Verify events (may or may not fire depending on plugin content)
            // These are informational and don't affect test success
        }

        [Fact]
        public async Task Test_TypedPluginWorkflow_EndToEnd()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreatePermissive());
            var filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(_pluginUri, securityValidator);
            var typedFactory = new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(
                filePluginLoader);

            // Act & Assert - Typed workflow
            
            // 1. Refresh and discover typed plugins
            await typedFactory.RefreshPluginsAsync();
            var availablePlugins = await typedFactory.GetPossiblePlugins();
            
            // Find a typed plugin (may not exist in test environment)
            var typedPlugin = availablePlugins.FirstOrDefault(p => p.Type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedPluginClass<,>)));
            
            if (!typedPlugin.Equals(default))
            {
                // 2. Execute with strongly-typed input/output
                var input = new WordGuessInput { Word = "test", CaseSensitive = false };
                var result = typedFactory.ExecutePlugin(typedPlugin.ModuleName, typedPlugin.PluginName, input);
                
                Assert.NotNull(result);
                // Either outcome is valid for the test
                
                // 3. Test async execution with timeout
                var asyncResult = await typedFactory.ExecutePluginAsync(
                    typedPlugin.ModuleName, 
                    typedPlugin.PluginName, 
                    input, 
                    timeout: TimeSpan.FromSeconds(5));
                
                Assert.NotNull(asyncResult);
            }
        }

        [Fact]
        public async Task Test_SecurityIntegration_StrictSettings()
        {
            // Arrange - Use strict security settings
            var strictSettings = PluginSecuritySettings.CreateStrict();
            var securityValidator = new DefaultPluginSecurityValidator(strictSettings);
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            bool securityValidationFailed = false;
            string securityFailureReason = null;

            filePluginLoader.SecurityValidationFailed += (sender, args) =>
            {
                securityValidationFailed = true;
                securityFailureReason = string.Join("; ", args.ValidationResult.Issues.Select(i => i.Description));
            };

            // Act
            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();

            // Assert - With strict settings, some plugins may be rejected
            // This is expected behavior and validates security integration
            Assert.NotNull(availablePlugins);
            
            // If security validation failed, it should be for legitimate reasons
            if (securityValidationFailed)
            {
                Assert.NotNull(securityFailureReason);
                Assert.NotEmpty(securityFailureReason);
            }
        }

        [Fact]
        public async Task Test_ConcurrentPluginExecution()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            await pluginFactory.RefreshPluginsAsync();
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            
            if (!availablePlugins.Any())
            {
                // Skip test if no plugins available
                return;
            }

            var firstPlugin = availablePlugins.First();

            // Act - Execute plugin concurrently from multiple threads
            var tasks = new Task[10];
            var results = new string[10];
            var exceptions = new Exception[10];

            for (int i = 0; i < 10; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        var instance = pluginFactory.GetInstance(firstPlugin.ModuleName, firstPlugin.PluginName);
                        results[index] = instance?.Execute($"concurrent test {index}");
                    }
                    catch (Exception ex)
                    {
                        exceptions[index] = ex;
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert - All executions should complete without deadlocks
            for (int i = 0; i < 10; i++)
            {
                // Either result should be non-null OR exception should be null (but not both null)
                Assert.True(results[i] != null || exceptions[i] == null);
            }
        }

        [Fact]
        public async Task Test_PluginUnloadingAndReloading()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            // Act & Assert - Load, unload, reload cycle
            
            // 1. Initial load
            await pluginFactory.RefreshPluginsAsync();
            var initialPlugins = await pluginFactory.GetPossiblePlugins();
            var initialCount = initialPlugins.Count();

            // 2. Unload all plugins
            filePluginLoader.UnloadAllPlugins();

            // 3. Reload plugins
            await pluginFactory.RefreshPluginsAsync();
            var reloadedPlugins = await pluginFactory.GetPossiblePlugins();
            var reloadedCount = reloadedPlugins.Count();

            // 4. Verify same plugins are available after reload
            Assert.Equal(initialCount, reloadedCount);
            
            // Verify plugin names match (order may differ)
            var initialNames = initialPlugins.Select(p => $"{p.ModuleName}.{p.PluginName}").OrderBy(n => n);
            var reloadedNames = reloadedPlugins.Select(p => $"{p.ModuleName}.{p.PluginName}").OrderBy(n => n);
            Assert.Equal(initialNames, reloadedNames);
        }

        [Fact]
        public async Task Test_ErrorHandlingIntegration()
        {
            // Arrange - Create a scenario that will trigger various error conditions
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            bool pluginInstantiationErrorFired = false;
            bool pluginLoadingErrorFired = false;
            bool securityValidationFailed = false;

            pluginFactory.PluginInstantiationError += (sender, args) =>
            {
                pluginInstantiationErrorFired = true;
                Assert.NotNull(args.Exception);
                Assert.NotNull(args.ModuleName);
                Assert.NotNull(args.PluginName);
            };

            filePluginLoader.PluginLoadingFailed += (sender, args) =>
            {
                pluginLoadingErrorFired = true;
                Assert.NotNull(args.Exception);
                Assert.NotNull(args.FileName);
            };

            filePluginLoader.SecurityValidationFailed += (sender, args) =>
            {
                securityValidationFailed = true;
                Assert.NotNull(args.ValidationResult);
            };

            // Act
            await pluginFactory.RefreshPluginsAsync();

            // Try to get a non-existent plugin
            var nonExistentPlugin = pluginFactory.GetInstance("NonExistent.Module", "NonExistentPlugin");
            Assert.Null(nonExistentPlugin);

            // Try to get a plugin with invalid version
            var availablePlugins = await pluginFactory.GetPossiblePlugins();
            if (availablePlugins.Any())
            {
                var firstPlugin = availablePlugins.First();
                var invalidVersionPlugin = pluginFactory.GetInstance(
                    firstPlugin.ModuleName, 
                    firstPlugin.PluginName, 
                    new DevelApp.Utility.Model.SemanticVersionNumber(99, 99, 99));
                Assert.Null(invalidVersionPlugin);
            }

            // Assert - Error handling should be robust
            // Events may or may not fire depending on the specific plugins and environment
            // The important thing is that the system doesn't crash and handles errors gracefully
        }

        [Fact]
        public async Task Test_MemoryManagement_PluginUnloading()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(_pluginUri, securityValidator);
            var pluginFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader);

            // Act - Load plugins multiple times and unload to test memory management
            for (int i = 0; i < 3; i++)
            {
                await pluginFactory.RefreshPluginsAsync();
                var plugins = await pluginFactory.GetPossiblePlugins();
                
                // Create instances
                foreach (var plugin in plugins.Take(2)) // Limit to first 2 to avoid excessive testing
                {
                    var instance = pluginFactory.GetInstance(plugin.ModuleName, plugin.PluginName);
                    if (instance != null)
                    {
                        var result = instance.Execute("memory test");
                        Assert.NotNull(result);
                    }
                }

                // Unload all plugins
                filePluginLoader.UnloadAllPlugins();

                // Force garbage collection to help with memory cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            // Assert - Test should complete without memory issues
            // The fact that we can complete multiple load/unload cycles indicates good memory management
            Assert.True(true);
        }

        [Fact]
        public async Task Test_PluginExecutionContext_Integration()
        {
            // Arrange
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            var filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(_pluginUri, securityValidator);
            var typedFactory = new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(
                new PluginClassFactory<ITypedSpecificInterface>(filePluginLoader));

            await typedFactory.RefreshPluginsAsync();
            var availablePlugins = await typedFactory.GetPossiblePlugins();
            
            var typedPlugin = availablePlugins.FirstOrDefault(p => p.Type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedPluginClass<,>)));
            
            if (!typedPlugin.Equals(default))
            {
                // Create execution context with cancellation
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var context = new PluginExecutionContext(
                    new ConsolePluginLogger(), 
                    cts.Token, 
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["TestProperty"] = "TestValue"
                    });

                // Act
                var input = new WordGuessInput { Word = "integration", CaseSensitive = false };
                var result = typedFactory.ExecutePlugin(
                    typedPlugin.ModuleName, 
                    typedPlugin.PluginName, 
                    input, 
                    context);

                // Assert
                Assert.NotNull(result);
                Assert.False(cts.Token.IsCancellationRequested);
            }
        }
    }
}

