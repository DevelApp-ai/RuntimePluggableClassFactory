using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using PluginImplementations;
using System;
using System.Collections.Generic;
using System.IO;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(retainOldVersions: 10);


            Uri pluginDirectory = new Uri("file:///E:/Projects/RuntimePluggableClassFactory/PluginFolder", UriKind.Absolute);
            if (!Directory.Exists(pluginDirectory.AbsolutePath))
            {
                Console.WriteLine("Directory NOT ok");
            }
            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            var allInstanceNames = pluginClassFactory.GetAllInstanceNamesDescriptionsAndVersions();

            Console.WriteLine("All stored instances are:");
            foreach(var var in allInstanceNames)
            {
                Console.WriteLine("Instance {0} Description {1} Versions [{2}]", var.Name, var.Description, ListToString(var.Versions));
            }
            Console.WriteLine("End of instances");

            #region Test 1.2.1

            ISpecificInterface instance = pluginClassFactory.GetInstance("SpecificClassImpl", "1.2.1");

            if (!instance.Execute("Mønster"))
            {
                Console.WriteLine("Result is Nay");
            }
            if (instance.Execute("Monster"))
            {
                Console.WriteLine("Result is Yay");
            }

            #endregion

            #region Test 1.2.2

            ISpecificInterface instance2 = pluginClassFactory.GetInstance("SpecificClassImpl", "1.2.2");

            if (!instance2.Execute("Mønster"))
            {
                Console.WriteLine("Result is Nay");
            }
            if (instance2.Execute("Monster"))
            {
                Console.WriteLine("Result is Yay");
            }

            #endregion

            #region Test 1.3.1

            ISpecificInterface instance3 = pluginClassFactory.GetInstance("SpecificClassImpl", "1.3.1");

            if (!instance.Execute("Mønster"))
            {
                Console.WriteLine("Result is Nay");
            }
            if (instance.Execute("SnuggleMonster"))
            {
                Console.WriteLine("Result is Yay");
            }

            #endregion

            #region Test 1.4.1

            ISpecificInterface instance4 = pluginClassFactory.GetInstance("SpecificClassImpl", "1.4.1");

            if (!instance.Execute("Mønster"))
            {
                Console.WriteLine("Result is Nay");
            }
            if (instance.Execute("CookieMonster"))
            {
                Console.WriteLine("Result is Yay");
            }

            #endregion

            Console.WriteLine("Finished. Press Any key to continue (Only [Enter] works though)");
            Console.ReadLine();
       }

        private static string ListToString(List<SemanticVersionNumber> versions)
        {
            string list = string.Empty;
            foreach(SemanticVersionNumber version in versions)
            {
                if(!list.Equals(string.Empty))
                {
                    list = list + "," + version.ToString();
                }
                else
                {
                    list = version.ToString();
                }
            }
            return list;
        }
    }
}
