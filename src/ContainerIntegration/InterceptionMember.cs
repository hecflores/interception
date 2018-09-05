using System;
using Unity.Registration;
using Unity.Storage;

namespace Unity.Interception.ContainerIntegration
{
    /// <summary>
    /// Stores information about a an intercepted object and configures a container accordingly.
    /// </summary>
    public abstract class InterceptionMember : IInjectionMember
    {
        public abstract void AddPolicies(Type serviceType, Type implementationType, string name, IPolicyList policies);

        public virtual bool BuildRequired { get; } = false;
    }
}
