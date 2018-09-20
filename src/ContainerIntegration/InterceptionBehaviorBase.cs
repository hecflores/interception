﻿using System;
using Unity.Build.Policy;
using Unity.Builder;
using Unity.Interception.ContainerIntegration.ObjectBuilder;
using Unity.Interception.InterceptionBehaviors;
using Unity.Policy.Lifetime;
using Unity.Storage;

namespace Unity.Interception.ContainerIntegration
{
    /// <summary>
    /// Base class for injection members that allow you to add
    /// interception behaviors.
    /// </summary>
    public abstract class InterceptionBehaviorBase : InterceptionMember, IRegisterPolicies
    {
        private readonly NamedTypeBuildKey _behaviorKey;
        private readonly IInterceptionBehavior _explicitBehavior;

        /// <summary>
        /// Initializes a new instance of the <see cref="IInterceptionBehavior"/> with a 
        /// <see cref="IInterceptionBehavior"/>.
        /// </summary>
        /// <param name="interceptionBehavior">The interception behavior to use.</param>
        protected InterceptionBehaviorBase(IInterceptionBehavior interceptionBehavior)
        {
            _explicitBehavior = interceptionBehavior ?? throw new ArgumentNullException(nameof(interceptionBehavior));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InterceptionBehavior"/> with a 
        /// given type/name pair.
        /// </summary>
        /// <param name="behaviorType">Type of behavior to </param>
        /// <param name="name"></param>
        protected InterceptionBehaviorBase(Type behaviorType, string name)
        {
            _behaviorKey = new NamedTypeBuildKey(behaviorType, name);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InterceptionBehavior"/> with a 
        /// given behavior type.
        /// </summary>
        /// <param name="behaviorType">Type of behavior to </param>
        protected InterceptionBehaviorBase(Type behaviorType)
            : this(behaviorType, null)
        {
        }

        /// <summary>
        /// Add policies to the <paramref name="policies"/> to configure the container to use the represented 
        /// <see cref="IInterceptionBehavior"/> for the supplied parameters.
        /// </summary>
        /// <param name="serviceType">Interface being registered.</param>
        /// <param name="implementationType">Type to register.</param>
        /// <param name="name">Name used to resolve the type object.</param>
        /// <param name="policies">Policy list to add policies to.</param>
        public void AddPolicies<T>(Type serviceType, Type implementationType, string name, ref T policies)
            where T: IPolicyList
        {
            if (_explicitBehavior != null)
            {
                var lifetimeManager = new ContainerControlledLifetimeManager();
                lifetimeManager.SetValue(_explicitBehavior);
                var behaviorName = Guid.NewGuid().ToString();
                var newBehaviorKey = new NamedTypeBuildKey(_explicitBehavior.GetType(), behaviorName);

                policies.Set(newBehaviorKey.Type, newBehaviorKey.Name, typeof(ILifetimePolicy), lifetimeManager);
                InterceptionBehaviorsPolicy behaviorsPolicy = GetBehaviorsPolicy(policies, serviceType, name);
                behaviorsPolicy.AddBehaviorKey(newBehaviorKey);
            }
            else
            {
                var behaviorsPolicy = GetBehaviorsPolicy(policies, serviceType, name);
                behaviorsPolicy.AddBehaviorKey(_behaviorKey);
            }
        }

        /// <summary>
        /// GetOrDefault the list of behaviors for the current type so that it can be added to.
        /// </summary>
        /// <param name="policies">Policy list.</param>
        /// <param name="implementationType">Implementation type to set behaviors for.</param>
        /// <param name="name">Name type is registered under.</param>
        /// <returns>An instance of <see cref="InterceptionBehaviorsPolicy"/>.</returns>
        protected abstract InterceptionBehaviorsPolicy GetBehaviorsPolicy(IPolicyList policies, Type implementationType, string name);
    }
}
