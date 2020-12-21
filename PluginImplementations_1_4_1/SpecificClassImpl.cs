using DevelApp.Utility.Model;
using System;

namespace PluginImplementations
{
    public class SpecificClassImpl : ISpecificInterface
    {
        public IdentifierString Name
        {
            get
            {
                return GetType().Name;
            }
        }

        public string Description
        {
            get
            {
                return "A test class for plugins";
            }
        }

        public SemanticVersionNumber Version
        {
            get
            {
                return GetType().Assembly.GetName().Version;
            }
        }

        public NamespaceString Module
        {
            get
            {
                return "Test";
            }
        }

        public bool Execute(string wordGuess)
        {
            return wordGuess.Equals("CookieMonster");
        }
    }
}
