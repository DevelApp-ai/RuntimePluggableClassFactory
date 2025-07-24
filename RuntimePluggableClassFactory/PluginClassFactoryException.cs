using System;

namespace DevelApp.RuntimePluggableClassFactory
{
    internal class PluginClassFactoryException : Exception
    {
        public PluginClassFactoryException()
        {
        }

        public PluginClassFactoryException(string message) : base(message)
        {
        }

        public PluginClassFactoryException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}