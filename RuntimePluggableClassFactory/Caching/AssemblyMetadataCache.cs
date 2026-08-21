using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace DevelApp.RuntimePluggableClassFactory.Caching
{
    /// <summary>
    /// Cache for assembly metadata to avoid repeated reflection calls
    /// Improves performance when loading plugins repeatedly
    /// </summary>
    public class AssemblyMetadataCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, AssemblyMetadata> _cache = new ConcurrentDictionary<string, AssemblyMetadata>();
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromHours(1);
        private bool _disposed = false;

        /// <summary>
        /// Gets or creates assembly metadata from cache
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly</param>
        /// <param name="factory">Factory function to create metadata if not in cache</param>
        /// <returns>Assembly metadata</returns>
        public AssemblyMetadata GetOrCreate(string assemblyPath, Func<string, AssemblyMetadata> factory)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssemblyMetadataCache));

            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            return _cache.GetOrAdd(assemblyPath, factory);
        }

        /// <summary>
        /// Gets assembly metadata from cache
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly</param>
        /// <returns>Assembly metadata or null if not found</returns>
        public AssemblyMetadata? Get(string assemblyPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssemblyMetadataCache));

            if (string.IsNullOrEmpty(assemblyPath))
                return null;

            _cache.TryGetValue(assemblyPath, out var metadata);
            return metadata;
        }

        /// <summary>
        /// Removes assembly metadata from cache
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly</param>
        /// <returns>True if removed, false if not found</returns>
        public bool Remove(string assemblyPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssemblyMetadataCache));

            return _cache.TryRemove(assemblyPath, out _);
        }

        /// <summary>
        /// Clears all cached metadata
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssemblyMetadataCache));

            _cache.Clear();
        }

        /// <summary>
        /// Gets the number of cached items
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Gets all cached assembly paths
        /// </summary>
        public string[] GetCachedAssemblyPaths() => _cache.Keys.ToArray();

        /// <summary>
        /// Creates assembly metadata from an assembly path
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly</param>
        /// <returns>Assembly metadata</returns>
        public static AssemblyMetadata CreateMetadata(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
            {
                return new AssemblyMetadata
                {
                    Path = assemblyPath,
                    Exists = false,
                    LastWriteTime = DateTime.MinValue
                };
            }

            var fileInfo = new FileInfo(assemblyPath);
            
            try
            {
                // Load assembly to get metadata (note: this loads the assembly into memory)
                var assembly = Assembly.LoadFrom(assemblyPath);
                
                return new AssemblyMetadata
                {
                    Path = assemblyPath,
                    Exists = true,
                    LastWriteTime = fileInfo.LastWriteTimeUtc,
                    FileSize = fileInfo.Length,
                    AssemblyName = assembly.GetName().Name,
                    AssemblyVersion = assembly.GetName().Version?.ToString(),
                    Types = assembly.GetTypes().Select(t => t.FullName ?? t.Name).ToArray(),
                    CreationTime = DateTime.UtcNow,
                    ExpirationTime = DateTime.UtcNow.AddHours(1)
                };
            }
            catch (Exception ex)
            {
                return new AssemblyMetadata
                {
                    Path = assemblyPath,
                    Exists = true,
                    LastWriteTime = fileInfo.LastWriteTimeUtc,
                    FileSize = fileInfo.Length,
                    Error = ex.Message,
                    CreationTime = DateTime.UtcNow,
                    ExpirationTime = DateTime.UtcNow.AddHours(1)
                };
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cache.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Metadata about an assembly
    /// </summary>
    public class AssemblyMetadata
    {
        /// <summary>
        /// Path to the assembly
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// Whether the assembly file exists
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// Last write time of the assembly file
        /// </summary>
        public DateTime LastWriteTime { get; set; }

        /// <summary>
        /// Size of the assembly file in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Assembly name
        /// </summary>
        public string? AssemblyName { get; set; }

        /// <summary>
        /// Assembly version
        /// </summary>
        public string? AssemblyVersion { get; set; }

        /// <summary>
        /// List of type names in the assembly
        /// </summary>
        public string[]? Types { get; set; }

        /// <summary>
        /// Error message if metadata loading failed
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// When the metadata was created
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// When the metadata expires
        /// </summary>
        public DateTime ExpirationTime { get; set; }

        /// <summary>
        /// Whether the metadata has expired
        /// </summary>
        public bool IsExpired => DateTime.UtcNow > ExpirationTime;
    }
}
