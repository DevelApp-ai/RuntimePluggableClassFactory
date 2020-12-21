using DevelApp.Utility.Model;
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
        IdentifierString Name { get; }

        /// <summary>
        /// Identifies the module in which the Name is unique
        /// </summary>
        NamespaceString Module { get; }

        /// <summary>
        /// Description of the plugin class used in the factory
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Returns the version of the plugin class to determine if it is a replacement of an existing as a hotfix or a new supported version
        /// </summary>
        SemanticVersionNumber Version { get; }
    }
}
