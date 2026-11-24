namespace Adversarial
{
    public class ProcessSpawner
    {
        public void Run()
        {
            System.Diagnostics.Process.Start("/bin/touch", "RuntimePluggableClassFactory.Benchmarks/data/20261124_sandbox/m4-sandbox/marker-process.txt");
        }
    }
}