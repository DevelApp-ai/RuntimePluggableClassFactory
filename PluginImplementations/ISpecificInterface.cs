using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace PluginImplementations
{
    public interface ISpecificInterface:IPluginClass
    {
        /// <summary>
        /// The specific executing interface
        /// </summary>
        /// <param name="wordGuess"></param>
        /// <returns></returns>
        bool Execute(string wordGuess);
    }
}
