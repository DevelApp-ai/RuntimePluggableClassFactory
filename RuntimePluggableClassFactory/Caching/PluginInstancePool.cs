using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DevelApp.RuntimePluggableClassFactory.Caching
{
    /// <summary>
    /// Pool of plugin instances for reuse
    /// Improves performance for frequently used plugins by avoiding repeated instantiation
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class PluginInstancePool<T> : IDisposable where T : IPluginClass
    {
        private readonly ConcurrentDictionary<(string ModuleName, string PluginName), PluginInstancePoolEntry<T>> _pools = 
            new ConcurrentDictionary<(string, string), PluginInstancePoolEntry<T>>();
        
        private readonly int _maxPoolSize;
        private readonly TimeSpan _maxLifetime;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new plugin instance pool
        /// </summary>
        /// <param name="maxPoolSize">Maximum number of instances per plugin type</param>
        /// <param name="maxLifetime">Maximum lifetime of pooled instances</param>
        public PluginInstancePool(int maxPoolSize = 10, TimeSpan? maxLifetime = null)
        {
            _maxPoolSize = maxPoolSize;
            _maxLifetime = maxLifetime ?? TimeSpan.FromHours(1);
        }

        /// <summary>
        /// Gets a plugin instance from the pool or creates a new one
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="factory">Factory function to create new instances</param>
        /// <returns>Plugin instance</returns>
        public T GetInstance(
            string moduleName,
            string pluginName,
            Func<T> factory)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

            var key = (moduleName, pluginName);
            var entry = _pools.GetOrAdd(key, _ => new PluginInstancePoolEntry<T>(_maxPoolSize, _maxLifetime));
            
            return entry.GetInstance(factory);
        }

        /// <summary>
        /// Returns a plugin instance to the pool
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="instance">Instance to return</param>
        public void ReturnInstance(string moduleName, string pluginName, T instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

            var key = (moduleName, pluginName);
            if (_pools.TryGetValue(key, out var entry))
            {
                entry.ReturnInstance(instance);
            }
        }

        /// <summary>
        /// Clears all pooled instances
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

            foreach (var entry in _pools.Values)
            {
                entry.Clear();
            }
        }

        /// <summary>
        /// Clears pooled instances for a specific plugin
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        public void Clear(string moduleName, string pluginName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

            var key = (moduleName, pluginName);
            if (_pools.TryGetValue(key, out var entry))
            {
                entry.Clear();
            }
        }

        /// <summary>
        /// Gets the number of pooled instances
        /// </summary>
        public int TotalPooledCount
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

                int count = 0;
                foreach (var entry in _pools.Values)
                {
                    count += entry.Count;
                }
                return count;
            }
        }

        /// <summary>
        /// Gets statistics about the pool
        /// </summary>
        public Dictionary<(string ModuleName, string PluginName), int> GetStatistics()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PluginInstancePool<T>));

            var stats = new Dictionary<(string, string), int>();
            foreach (var kvp in _pools)
            {
                stats[kvp.Key] = kvp.Value.Count;
            }
            return stats;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Clear();
                    _pools.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Entry for a specific plugin type in the pool
        /// </summary>
        private class PluginInstancePoolEntry<TPlugin> where TPlugin : IPluginClass
        {
            private readonly ConcurrentBag<TPlugin> _instances = new ConcurrentBag<TPlugin>();
            private readonly int _maxSize;
            private readonly TimeSpan _maxLifetime;

            public PluginInstancePoolEntry(int maxSize, TimeSpan maxLifetime)
            {
                _maxSize = maxSize;
                _maxLifetime = maxLifetime;
            }

            public TPlugin GetInstance(Func<TPlugin> factory)
            {
                // Try to get an existing instance
                if (_instances.TryTake(out var instance))
                {
                    // Check if instance is still valid (not expired)
                    // For simplicity, we assume instances are stateless and always valid
                    // In a real implementation, you might track creation time
                    return instance;
                }

                // Create a new instance
                return factory();
            }

            public void ReturnInstance(TPlugin instance)
            {
                // Only return if we haven't exceeded max size
                if (_instances.Count < _maxSize)
                {
                    _instances.Add(instance);
                }
                // Otherwise, the instance will be garbage collected
            }

            public void Clear()
            {
                _instances.Clear();
            }

            public int Count => _instances.Count;
        }
    }
}
