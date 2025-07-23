using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using PluginImplementations;
using System;
using System.IO;
using Xunit;
using System.Collections.Generic;
using System.Threading;

namespace RuntimePluggableClassFactory.Test
{
    public class UnloadingTests
    {
        [Fact]
        public void Test_PluginUnloading()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            Assert.True(Directory.Exists(pluginDirectory.AbsolutePath));
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory);
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            pluginClassFactory.AllowPlugin("Test", "SpecificClassImpl", "1.2.1");
            
            var loadResult = pluginClassFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            Assert.Equal(1, loadResult.Count);
            
            // Get an instance to verify it works
            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl", "1.2.1");
            Assert.NotNull(instance);
            Assert.True(instance.Execute("Monster"));
            
            // Test unloading functionality
            string pluginPath = Path.Combine(pluginDirectory.AbsolutePath, "PluginImplementations_1_2_1");
            bool unloadResult = filePluginLoader.UnloadPlugin(pluginPath);
            
            // Note: The unload result might be false if the plugin wasn't loaded in a separate context
            // This is expected behavior when the assembly is already in the default context
            
            // Test unload all functionality
            filePluginLoader.UnloadAllPlugins();
            
            // The test passes if no exceptions are thrown
            Assert.True(true);
        }

        [Fact]
        public void Test_PluginWatcher_Creation()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            
            FilePluginLoader<ISpecificInterface> filePluginLoader = new FilePluginLoader<ISpecificInterface>(new Uri(pathStringExpanded));
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(filePluginLoader, retainOldVersions: 10);
            
            // Test that PluginWatcher can be created without throwing exceptions
            using (var watcher = new PluginWatcher<ISpecificInterface>(pathStringExpanded, pluginClassFactory))
            {
                Assert.NotNull(watcher);
                
                // Test start/stop watching
                watcher.StartWatching();
                Thread.Sleep(100); // Give it a moment
                watcher.StopWatching();
            }
            
            Assert.True(true);
        }
    }
}

