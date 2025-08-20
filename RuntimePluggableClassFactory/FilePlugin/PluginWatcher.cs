using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.FilePlugin
{
    /// <summary>
    /// Watches for plugin changes in the file system and triggers reload events
    /// Implements TDS requirement for runtime plugin detection
    /// </summary>
    public class PluginWatcher<T> : IDisposable where T : IPluginClass
    {
        private readonly FileSystemWatcher _watcher;
        private readonly PluginClassFactory<T> _factory;
        private readonly string _pluginPath;
        private bool _disposed = false;

        /// <summary>
        /// Event fired when plugins are detected as changed
        /// </summary>
        public event EventHandler<PluginChangedEventArgs> PluginChanged;

        public PluginWatcher(string pluginPath, PluginClassFactory<T> factory)
        {
            _pluginPath = pluginPath ?? throw new ArgumentNullException(nameof(pluginPath));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            if (!Directory.Exists(pluginPath))
            {
                throw new DirectoryNotFoundException($"Plugin directory not found: {pluginPath}");
            }

            _watcher = new FileSystemWatcher(pluginPath)
            {
                Filter = "*.dll",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName
            };

            _watcher.Created += OnPluginFileChanged;
            _watcher.Changed += OnPluginFileChanged;
            _watcher.Deleted += OnPluginFileDeleted;
            _watcher.Renamed += OnPluginFileRenamed;
        }

        /// <summary>
        /// Starts monitoring for plugin changes
        /// </summary>
        public void StartWatching()
        {
            if (!_disposed)
            {
                _watcher.EnableRaisingEvents = true;
            }
        }

        /// <summary>
        /// Stops monitoring for plugin changes
        /// </summary>
        public void StopWatching()
        {
            if (!_disposed)
            {
                _watcher.EnableRaisingEvents = false;
            }
        }

        private async void OnPluginFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Wait a bit to ensure file is fully written
                await Task.Delay(500);

                // Trigger plugin refresh
                var result = await _factory.RefreshPluginsAsync();
                
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    RefreshResult = result
                });
            }
            catch (Exception ex)
            {
                // Log error but don't crash the watcher
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    Error = ex
                });
            }
        }

        private async void OnPluginFileDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                // For deleted files, we might want to unload the specific plugin
                if (_factory.PluginLoader is FilePluginLoader<T> fileLoader)
                {
                    var pluginDir = Path.GetDirectoryName(e.FullPath);
                    if (pluginDir != null)
                    {
                        fileLoader.UnloadPlugin(pluginDir);
                    }
                }

                // Refresh the factory to remove deleted plugins
                var result = await _factory.RefreshPluginsAsync();
                
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    RefreshResult = result
                });
            }
            catch (Exception ex)
            {
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    Error = ex
                });
            }
        }

        private async void OnPluginFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                // Handle rename as delete + create
                if (_factory.PluginLoader is FilePluginLoader<T> fileLoader)
                {
                    var oldPluginDir = Path.GetDirectoryName(e.OldFullPath);
                    if (oldPluginDir != null)
                    {
                        fileLoader.UnloadPlugin(oldPluginDir);
                    }
                }

                var result = await _factory.RefreshPluginsAsync();
                
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    OldFullPath = e.OldFullPath,
                    OldName = e.OldName,
                    RefreshResult = result
                });
            }
            catch (Exception ex)
            {
                PluginChanged?.Invoke(this, new PluginChangedEventArgs
                {
                    ChangeType = e.ChangeType,
                    FullPath = e.FullPath,
                    Name = e.Name,
                    OldFullPath = e.OldFullPath,
                    OldName = e.OldName,
                    Error = ex
                });
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _watcher?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for plugin change notifications
    /// </summary>
    public class PluginChangedEventArgs : EventArgs
    {
        public WatcherChangeTypes ChangeType { get; set; }
        public string? FullPath { get; set; }
        public string? Name { get; set; }
        public string? OldFullPath { get; set; }
        public string? OldName { get; set; }
        public (bool Success, int Count)? RefreshResult { get; set; }
        public Exception? Error { get; set; }
    }
}

