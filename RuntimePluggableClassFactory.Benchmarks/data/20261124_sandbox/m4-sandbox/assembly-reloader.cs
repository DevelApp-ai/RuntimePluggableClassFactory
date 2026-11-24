namespace Adversarial
{
    public class AssemblyReloader
    {
        public void Run()
        {
            System.Reflection.Assembly.LoadFrom("RuntimePluggableClassFactory.Benchmarks/data/20261124_sandbox/m4-sandbox/not-a-real-plugin.dll");
        }
    }
}