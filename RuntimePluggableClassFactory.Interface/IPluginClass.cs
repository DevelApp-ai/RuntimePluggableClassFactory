using System;
using System.Collections.Generic;
using System.Text;

namespace DevelApp.RuntimePluggableClassFactory.Interface
{
    /// <summary>
    /// Base interface for plugin factory
    /// </summary>
    public interface IPluginClass
    {
        /// <summary>
        /// Unique name used to identify the plugin class
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of the plugin class used in the factory
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Returns the version of the plugin class to determine if it is a replacement of an existing as a hotfix or a new supported version
        /// </summary>
        int Version { get; }
    }
}
