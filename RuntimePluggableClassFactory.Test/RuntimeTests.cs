using DevelApp.RuntimePluggableClassFactory;
using PluginImplementations;
using System;
using System.IO;
using Xunit;
using System.Collections.Generic;

namespace RuntimePluggableClassFactory.Test
{
    public class RuntimeTests
    {
        [Fact]
        public void Test_1_2_1()
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(retainOldVersions: 10);

            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            Assert.True(Directory.Exists(pluginDirectory.AbsolutePath));

            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl", "1.2.1");
            Assert.False(instance.Execute("Mønster"));
            Assert.True(instance.Execute("Monster"));

        }

        [Fact]
        public void Test_1_2_2()
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(retainOldVersions: 10);

            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            Assert.True(Directory.Exists(pluginDirectory.AbsolutePath));

            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl2", "1.2.2");
            Assert.False(instance.Execute("Mønster"));
            Assert.True(instance.Execute("Monster"));

        }

        [Fact]
        public void Test_1_3_1()
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(retainOldVersions: 10);

            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            Assert.True(Directory.Exists(pluginDirectory.AbsolutePath));

            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl3", "1.3.1");
            Assert.False(instance.Execute("Mønster"));
            Assert.True(instance.Execute("SnuggleMonster"));

        }

        [Fact]
        public void Test_1_4_1()
        {
            PluginClassFactory<ISpecificInterface> pluginClassFactory = new PluginClassFactory<ISpecificInterface>(retainOldVersions: 10);

            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            Assert.True(Directory.Exists(pluginDirectory.AbsolutePath));

            pluginClassFactory.LoadFromDirectory(pluginDirectory);

            ISpecificInterface instance = pluginClassFactory.GetInstance("Test", "SpecificClassImpl4", "1.4.1");
            Assert.False(instance.Execute("Mønster"));
            Assert.True(instance.Execute("CookieMonster"));

        }
    }
}
