using System.Reflection;
using Unity.Build.Context;
using Unity.Policy;

namespace Unity.Interception.Interceptors.TypeInterceptors.VirtualMethodInterception
{
    /// <summary>
    /// A small implementation of <see cref="IConstructorSelectorPolicy"/> that returns the
    /// given <see cref="IConstructorSelectorPolicy"/> object.
    /// </summary>
    public class ConstructorSelectorPolicyWithResolverKeysSelectorPolicy : IConstructorSelectorPolicy
    {
        private readonly ConstructorInfo _selectedConstructor;

        /// <summary>
        /// Create a new <see cref="ConstructorSelectorPolicyWithResolverKeysSelectorPolicy"/> instance.
        /// </summary>
        /// <param name="selectedConstructor">Information about which constructor to select.</param>
        public ConstructorSelectorPolicyWithResolverKeysSelectorPolicy(ConstructorInfo selectedConstructor)
        {
            _selectedConstructor = selectedConstructor;
        }

        public object SelectConstructor<TContext>(ref TContext context) where TContext : IBuildContext
        {
            return _selectedConstructor;
        }
    }
}
