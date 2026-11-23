[assembly: Adversarial.SecurityPermissionAttribute]

namespace Adversarial
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
    public sealed class SecurityPermissionAttribute : System.Attribute
    {
    }
}