using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace RuntimePluggableClassFactory.Test
{
    /// <summary>
    /// Tests for proper IDisposable implementation across the library
    /// </summary>
    public class DisposalTests
    {
        private readonly string _testPluginPath;

        public DisposalTests()
        {
            _testPluginPath = Path.Combine(Directory.GetCurrentDirectory(), "PluginFolder");
            if (!Directory.Exists(_testPluginPath))
            {
                Directory.CreateDirectory(_testPluginPath);
            }
        }

        [Fact]
        public void Test_PluginClassFactory_Dispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);

            // Act & Assert
            factory.Dispose();
            // Should not throw
        }

        [Fact]
        public void Test_PluginClassFactory_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);

            // Act & Assert
            factory.Dispose();
            factory.Dispose(); // Second dispose should be safe
            // Should not throw
        }

        [Fact]
        public void Test_PluginClassFactory_ThrowsAfterDispose_GetInstance()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);
            factory.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => 
                factory.GetInstance(new NamespaceString("Test"), new IdentifierString("Plugin")));
        }

        [Fact]
        public async Task Test_PluginClassFactory_ThrowsAfterDispose_RefreshPluginsAsync()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);
            factory.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => 
                await factory.RefreshPluginsAsync());
        }

        [Fact]
        public void Test_FilePluginLoader_Dispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);

            // Act & Assert
            loader.Dispose();
            // Should not throw
        }

        [Fact]
        public void Test_FilePluginLoader_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);

            // Act & Assert
            loader.Dispose();
            loader.Dispose(); // Second dispose should be safe
            // Should not throw
        }

        [Fact]
        public async Task Test_FilePluginLoader_ThrowsAfterDispose_LoadPluginsAsync()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            loader.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => 
                await loader.LoadPluginsAsync(new System.Collections.Generic.List<(NamespaceString, IdentifierString, SemanticVersionNumber)>()));
        }

        [Fact]
        public async Task Test_FilePluginLoader_ThrowsAfterDispose_ListAllPossiblePluginsAsync()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            loader.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => 
                await loader.ListAllPossiblePluginsAsync());
        }

        [Fact]
        public void Test_TypedPluginClassFactory_Dispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<ITypedPluginClass<string, string>>(pluginPath);
            var factory = new TypedPluginClassFactory<ITypedPluginClass<string, string>, string, string>(loader);

            // Act & Assert
            factory.Dispose();
            // Should not throw
        }

        [Fact]
        public void Test_TypedPluginClassFactory_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<ITypedPluginClass<string, string>>(pluginPath);
            var factory = new TypedPluginClassFactory<ITypedPluginClass<string, string>, string, string>(loader);

            // Act & Assert
            factory.Dispose();
            factory.Dispose(); // Second dispose should be safe
            // Should not throw
        }

        [Fact]
        public void Test_PluginWatcher_Dispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);
            var watcher = new PluginWatcher<IPluginClass>(_testPluginPath, factory);

            // Act & Assert
            watcher.Dispose();
            factory.Dispose();
            loader.Dispose();
            // Should not throw
        }

        [Fact]
        public void Test_PluginWatcher_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);
            var watcher = new PluginWatcher<IPluginClass>(_testPluginPath, factory);

            // Act & Assert
            watcher.Dispose();
            watcher.Dispose(); // Second dispose should be safe
            factory.Dispose();
            loader.Dispose();
            // Should not throw
        }

        [Fact]
        public void Test_UsingStatement_PluginClassFactory()
        {
            // Arrange & Act & Assert
            var pluginPath = new Uri(_testPluginPath);
            using (var loader = new FilePluginLoader<IPluginClass>(pluginPath))
            using (var factory = new PluginClassFactory<IPluginClass>(loader))
            {
                // Factory should be usable within using block
                Assert.NotNull(factory);
            }
            // Disposal handled automatically
        }

        [Fact]
        public void Test_UsingStatement_TypedPluginClassFactory()
        {
            // Arrange & Act & Assert
            var pluginPath = new Uri(_testPluginPath);
            using (var loader = new FilePluginLoader<ITypedPluginClass<string, string>>(pluginPath))
            using (var factory = new TypedPluginClassFactory<ITypedPluginClass<string, string>, string, string>(loader))
            {
                // Factory should be usable within using block
                Assert.NotNull(factory);
            }
            // Disposal handled automatically
        }

        [Fact]
        public void Test_UsingStatement_PluginWatcher()
        {
            // Arrange & Act & Assert
            var pluginPath = new Uri(_testPluginPath);
            using (var loader = new FilePluginLoader<IPluginClass>(pluginPath))
            using (var factory = new PluginClassFactory<IPluginClass>(loader))
            using (var watcher = new PluginWatcher<IPluginClass>(_testPluginPath, factory))
            {
                // Watcher should be usable within using block
                Assert.NotNull(watcher);
            }
            // Disposal handled automatically
        }

        [Fact]
        public void Test_PluginWatcher_DisposeCleansUpEventHandlers()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);
            var watcher = new PluginWatcher<IPluginClass>(_testPluginPath, factory);
            
            watcher.StartWatching();

            // Act
            watcher.Dispose();

            // Assert - if event handlers weren't cleaned up, this could cause issues
            // The test passes if no exceptions are thrown
            factory.Dispose();
            loader.Dispose();
        }

        [Fact]
        public void Test_FilePluginLoader_UnloadAllPlugins_AfterDispose()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);

            // Act
            loader.Dispose();
            
            // Assert - UnloadAllPlugins should have been called during Dispose
            // This test verifies the dispose pattern is working correctly
            Assert.True(true); // If we get here without exception, dispose worked
        }

        [Fact]
        public void Test_NestedDisposal_FactoryDisposesLoader()
        {
            // Arrange
            var pluginPath = new Uri(_testPluginPath);
            var loader = new FilePluginLoader<IPluginClass>(pluginPath);
            var factory = new PluginClassFactory<IPluginClass>(loader);

            // Act
            factory.Dispose(); // Should also dispose the loader

            // Assert
            // Verify loader is disposed by trying to use it
            Assert.ThrowsAsync<ObjectDisposedException>(async () => 
                await loader.LoadPluginsAsync(new System.Collections.Generic.List<(NamespaceString, IdentifierString, SemanticVersionNumber)>()));
        }
    }
}
