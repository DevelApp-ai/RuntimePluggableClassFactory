using System.IO;

namespace Adversarial
{
    public class FileStreamField
    {
        public FileStream Backdoor;

        public void Run()
        {
            File.Create("RuntimePluggableClassFactory.Benchmarks/data/20261124_sandbox/m4-sandbox/marker-filestream.txt").Dispose();
        }
    }
}