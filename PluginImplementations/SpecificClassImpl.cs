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
                return "1.2.1";
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
            return wordGuess.Equals("Monster");
        }
    }
    public class SpecificClassImpl2 : ISpecificInterface
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
                return "1.2.2";
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
            return wordGuess.Equals("Monster");
        }
    }

    public class SpecificClassImpl3 : ISpecificInterface
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
                return "1.3.1";
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
            return wordGuess.Equals("SnuggleMonster");
        }
    }
    public class SpecificClassImpl4 : ISpecificInterface
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
                return "1.4.1";
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
