using System;
using System.Runtime.Serialization;

namespace DevelApp.RuntimePluggableClassFactory
{
    [Serializable]
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

        protected PluginClassFactoryException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}