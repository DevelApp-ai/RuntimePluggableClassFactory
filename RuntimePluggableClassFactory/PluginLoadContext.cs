using System;
using System.Reflection;
using System.Runtime.Loader;

namespace DevelApp.RuntimePluggableClassFactory
{
    /// <summary>
    /// Collectible AssemblyLoadContext for plugin loading with unloading support
    /// Taken from the https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
    /// Enhanced for TDS requirements with collectible support
    /// </summary>
    internal class PluginLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
