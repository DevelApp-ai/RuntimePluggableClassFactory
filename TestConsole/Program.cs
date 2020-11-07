using DevelApp.RuntimePluggableClassFactory;
using PluginImplementations;
using System;
using System.IO;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>();
            string pluginDirectory = @"..\..\..\..\PluginFolder";
            if (!Directory.Exists(pluginDirectory))
            {
                Console.WriteLine("Directory NOT ok");
            }
            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            Console.WriteLine("Finished. Press Any key to continue (Only [Enter] works though)");
            Console.ReadLine();
        }
    }
}
