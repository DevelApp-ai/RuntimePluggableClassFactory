using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Interface;
using PluginImplementations;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Interface = DevelApp.RuntimePluggableClassFactory.Interface;

namespace RuntimePluggableClassFactory.Test
{
    public class TypedPluginTests
    {
        [Fact]
        public void Test_TypedPlugin_Success()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = typedFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            // Test with correct word
            var input = new WordGuessInput 
            { 
                Word = "Monster", 
                CaseSensitive = false 
            };
            
            var result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input);
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.IsCorrect);
            Assert.Equal(1.0, result.Data.ConfidenceScore);
            Assert.Contains("Correct", result.Data.Message);
            Assert.True(result.Data.ProcessingDuration.TotalMilliseconds >= 0);
        }

        [Fact]
        public void Test_TypedPlugin_Failure()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = typedFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            // Test with incorrect word
            var input = new WordGuessInput 
            { 
                Word = "WrongWord", 
                CaseSensitive = false 
            };
            
            var result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input);
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.False(result.Data.IsCorrect);
            Assert.Equal(0.0, result.Data.ConfidenceScore);
            Assert.Contains("Incorrect", result.Data.Message);
        }

        [Fact]
        public void Test_TypedPlugin_CaseSensitive()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = typedFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            // Test case sensitive - should fail with lowercase
            var input = new WordGuessInput 
            { 
                Word = "monster", 
                CaseSensitive = true 
            };
            
            var result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input);
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.False(result.Data.IsCorrect);
            
            // Test case insensitive - should succeed with lowercase
            input.CaseSensitive = false;
            result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input);
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.IsCorrect);
        }

        [Fact]
        public void Test_TypedPlugin_WithContext()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = typedFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            // Create custom context with cancellation token
            using (var cts = new CancellationTokenSource())
            {
                var context = new PluginExecutionContext(
                    new NullPluginLogger(), 
                    cts.Token);
                
                var input = new WordGuessInput { Word = "Monster" };
                var result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input, context);
                
                Assert.True(result.Success);
                Assert.NotNull(result.Data);
                Assert.True(result.Data.IsCorrect);
            }
        }

        [Fact]
        public async Task Test_TypedPlugin_Async()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = await typedFactory.RefreshPluginsAsync();
            Assert.True(loadResult.Success);
            
            var input = new WordGuessInput { Word = "Monster" };
            var result = await typedFactory.ExecutePluginAsync("Test", "TypedSpecificClassImpl", input);
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.IsCorrect);
        }

        [Fact]
        public async Task Test_TypedPlugin_WithTimeout()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = await typedFactory.RefreshPluginsAsync();
            Assert.True(loadResult.Success);
            
            var input = new WordGuessInput { Word = "Monster" };
            var result = await typedFactory.ExecutePluginAsync("Test", "TypedSpecificClassImpl", input, timeout: TimeSpan.FromSeconds(5));
            
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.IsCorrect);
        }

        [Fact]
        public void Test_TypedPlugin_InvalidInput()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            typedFactory.AllowPlugin("Test", "TypedSpecificClassImpl", "1.2.1");
            var loadResult = typedFactory.RefreshPluginsAsync().Result;
            Assert.True(loadResult.Success);
            
            // Test with null/empty word
            var input = new WordGuessInput { Word = null };
            var result = typedFactory.ExecutePlugin("Test", "TypedSpecificClassImpl", input);
            
            Assert.False(result.Success);
            Assert.Contains("null or empty", result.ErrorMessage);
        }

        [Fact]
        public void Test_TypedPlugin_NonExistentPlugin()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            FilePluginLoader<ITypedSpecificInterface> filePluginLoader = new FilePluginLoader<ITypedSpecificInterface>(pluginDirectory);
            TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput> typedFactory = 
                new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(filePluginLoader, retainOldVersions: 10);
            
            var input = new WordGuessInput { Word = "Monster" };
            var result = typedFactory.ExecutePlugin("NonExistent", "NonExistent", input);
            
            Assert.False(result.Success);
            Assert.Contains("Plugin not found", result.ErrorMessage);
        }
    }
}

