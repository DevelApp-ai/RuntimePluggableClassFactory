using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using PluginImplementations;
using System;
using System.IO;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Test
{
    public class StabilityTests
    {
        [Fact]
        public void Test_PluginExecutionSandbox_Success()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            pluginClassFactory.AllowPlugin("Test", "SpecificClassImpl", "1.2.1");
            var loadResult = pluginClassFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl", "1.2.1");
            Assert.NotNull(instance);
            
            // Test successful execution in sandbox
            var result = PluginExecutionSandbox.ExecuteSafely(instance, plugin => plugin.Execute("Monster"));
            
            Assert.True(result.Success);
            Assert.True(result.Result);
            Assert.Null(result.Error);
            Assert.NotNull(result.PluginName);
            Assert.True(result.Duration.TotalMilliseconds >= 0);
        }

        [Fact]
        public void Test_PluginExecutionSandbox_WithTimeout()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            pluginClassFactory.AllowPlugin("Test", "SpecificClassImpl", "1.2.1");
            var loadResult = pluginClassFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl", "1.2.1");
            Assert.NotNull(instance);
            
            // Test execution with timeout (should succeed quickly)
            var result = PluginExecutionSandbox.ExecuteSafely(instance, plugin => plugin.Execute("Monster"), TimeSpan.FromSeconds(5));
            
            Assert.True(result.Success);
            Assert.True(result.Result);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task Test_PluginExecutionSandbox_Async()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            pluginClassFactory.AllowPlugin("Test", "SpecificClassImpl", "1.2.1");
            var loadResult = await pluginClassFactory.RefreshPluginsAsync();
            Assert.True(loadResult.Success);
            
            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl", "1.2.1");
            Assert.NotNull(instance);
            
            // Test async execution in sandbox
            var result = await PluginExecutionSandbox.ExecuteSafelyAsync(instance, plugin => Task.FromResult(plugin.Execute("Monster")));
            
            Assert.True(result.Success);
            Assert.True(result.Result);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Test_PluginInstantiationErrorHandling()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            bool errorEventFired = false;
            pluginClassFactory.PluginInstantiationFailed += (sender, args) =>
            {
                errorEventFired = true;
                Assert.NotNull(args.Exception);
                Assert.NotNull(args.ModuleName);
                Assert.NotNull(args.PluginName);
            };
            
            // Try to get a non-existent plugin - should not crash but return null
            ISpecificInterface instance = pluginClassFactory.GetInstance("NonExistent", "NonExistent");
            Assert.Null(instance);
            
            // The error event should not fire for non-existent plugins, only for instantiation failures
            Assert.False(errorEventFired);
        }

        [Fact]
        public void Test_PluginLoadingErrorHandling()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            
            filePluginLoader.PluginLoadingFailed += (sender, args) =>
            {
                Assert.NotNull(args.Exception);
                Assert.NotNull(args.FileName);
                Assert.NotNull(args.PluginPath);
            };
            
            // This test verifies that the error handling infrastructure is in place
            // Actual errors would be triggered by corrupted DLLs or other loading issues
            var possiblePlugins = filePluginLoader.GetPossiblePlugins().Result;
            
            // Test passes if no exceptions are thrown during plugin discovery
            Assert.NotNull(possiblePlugins);
        }
    }
}

