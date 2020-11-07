using System;

namespace PluginImplementations
{
    public class SpecificClassImpl : ISpecificInterface
    {
        public string Name
        {
            get
            {
                return GetType().FullName;
            }
        }

        public string Description
        {
            get
            {
                return "A test class for plugins";
            }
        }

        public int Version
        {
            get
            {
                return 1;
            }
        }

        public bool Execute(string wordGuess)
        {
            return wordGuess.Equals("Monster");
        }
    }
}
