using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Build.Context;
using Unity.Builder;
using Unity.Interception.InterceptionBehaviors;
using Unity.Interception.Interceptors;
using Unity.Policy;
using Unity.Strategies;

namespace Unity.Interception.ContainerIntegration.ObjectBuilder
{
    /// <summary>
    /// A <see cref="BuilderStrategy"/> that hooks up type interception. It looks for
    /// a <see cref="ITypeInterceptionPolicy"/> for the current build key, or the current
    /// build type. If present, it substitutes types so that that proxy class gets
    /// built up instead. On the way back, it hooks up the appropriate handlers.
    /// </summary>
    public class TypeInterceptionStrategy : BuilderStrategy
    {
        #region BuilderStrategy

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PreBuildUp method is called when the chain is being executed in the
        /// forward direction.
        /// </summary>
        /// <remarks>In this class, PreBuildUp is responsible for figuring out if the
        /// class is proxyable, and if so, replacing it with a proxy class.</remarks>
        /// <param name="context">Context of the build operation.</param>
        public override void PreBuildUp<TContext>(ref TContext context)
        {
            if (context.Existing != null)
            {
                return;
            }

            Type typeToBuild = context.BuildKey.Type;

            var interceptionPolicy = FindInterceptionPolicy<TContext, ITypeInterceptionPolicy>(ref context);
            if (interceptionPolicy == null)
            {
                return;
            }

            var interceptor = interceptionPolicy.GetInterceptor(ref context);
            if (!interceptor.CanIntercept(typeToBuild))
            {
                return;
            }

            var interceptionBehaviorsPolicy = FindInterceptionPolicy<TContext, IInterceptionBehaviorsPolicy>(ref context);

            IEnumerable<IInterceptionBehavior> interceptionBehaviors =
                interceptionBehaviorsPolicy == null
                    ?
                        Enumerable.Empty<IInterceptionBehavior>()
                    :
                        interceptionBehaviorsPolicy.GetEffectiveBehaviors(
                            ref context, interceptor, typeToBuild, typeToBuild)
                        .Where(ib => ib.WillExecute);

            IAdditionalInterfacesPolicy additionalInterfacesPolicy =
                FindInterceptionPolicy<TContext, IAdditionalInterfacesPolicy>(ref context);

            IEnumerable<Type> additionalInterfaces =
                additionalInterfacesPolicy != null ? additionalInterfacesPolicy.AdditionalInterfaces : Type.EmptyTypes;

            var enumerable = interceptionBehaviors as IInterceptionBehavior[] ?? interceptionBehaviors.ToArray();
            context.Registration.Set(typeof(EffectiveInterceptionBehaviorsPolicy), 
                new EffectiveInterceptionBehaviorsPolicy { Behaviors = enumerable });

            Type[] allAdditionalInterfaces =
                Intercept.GetAllAdditionalInterfaces(enumerable, additionalInterfaces);

            Type interceptingType =
                interceptor.CreateProxyType(typeToBuild, allAdditionalInterfaces);

            DerivedTypeConstructorSelectorPolicySelectorPolicy.SetPolicyForInterceptingType(ref context, interceptingType);
        }

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <remarks>In this class, PostBuildUp checks to see if the object was proxyable,
        /// and if it was, wires up the handlers.</remarks>
        /// <param name="context">Context of the build operation.</param>
        public override void PostBuildUp<TContext>(ref TContext context)
        {
            IInterceptingProxy proxy = context.Existing as IInterceptingProxy;

            if (proxy == null)
            {
                return;
            }

            var effectiveInterceptionBehaviorsPolicy =
                (EffectiveInterceptionBehaviorsPolicy)context.Policies
                                                             .Get(context.OriginalBuildKey.Type, 
                                                                  context.OriginalBuildKey.Name, 
                                               typeof(EffectiveInterceptionBehaviorsPolicy));
            if (effectiveInterceptionBehaviorsPolicy == null)
            {
                return;
            }

            foreach (var interceptionBehavior in effectiveInterceptionBehaviorsPolicy.Behaviors)
            {
                proxy.AddInterceptionBehavior(interceptionBehavior);
            }
        }


        private static TPolicy FindInterceptionPolicy<TContext, TPolicy>(ref TContext context)
            where TContext : IBuilderContext
            where TPolicy : class, IBuilderPolicy
        {
            return (TPolicy)context.Policies.GetOrDefault(typeof(TPolicy), context.OriginalBuildKey) ??
                   (TPolicy)context.Policies.GetOrDefault(typeof(TPolicy), context.OriginalBuildKey.Type);
        }

        #endregion


        #region IRegisterTypeStrategy

        // TODO:
        //public void RegisterType(IContainerContext context, Type typeFrom, Type typeTo, string name, 
        //                         LifetimeManager lifetimeManager, params InjectionMember[] injectionMembers)
        //{
        //    Type typeToBuild = typeFrom ?? typeTo;

        //    var policy = (ITypeInterceptionPolicy)(context.Policies.Get(typeToBuild, name, typeof(ITypeInterceptionPolicy), out _) ??
        //                                           context.Policies.Get(typeToBuild, string.Empty, typeof(ITypeInterceptionPolicy), out _));
        //    if (policy == null) return;

        //    var interceptor = policy.GetInterceptor(context.Container);
        //    if (typeof(VirtualMethodInterceptor) == interceptor?.GetType())
        //        context.Policies.Set(typeToBuild, name, typeof(IBuildPlanPolicy), new OverriddenBuildPlanMarkerPolicy());
        //}

        #endregion


        #region Nested Types


        private class EffectiveInterceptionBehaviorsPolicy : IBuilderPolicy
        {
            public EffectiveInterceptionBehaviorsPolicy()
            {
                Behaviors = new List<IInterceptionBehavior>();
            }

            public IEnumerable<IInterceptionBehavior> Behaviors { get; set; }
        }

        internal class DerivedTypeConstructorSelectorPolicySelectorPolicy : IConstructorSelectorPolicy
        {
            internal readonly Type _interceptingType;
            internal readonly IConstructorSelectorPolicy OriginalConstructorSelectorPolicySelectorPolicy;

            internal DerivedTypeConstructorSelectorPolicySelectorPolicy(
                Type interceptingType,
                IConstructorSelectorPolicy originalConstructorSelectorPolicySelectorPolicy)
            {
                _interceptingType = interceptingType;
                OriginalConstructorSelectorPolicySelectorPolicy = originalConstructorSelectorPolicySelectorPolicy;
            }

            private static ConstructorInfo FindNewConstructor(ConstructorInfo originalConstructor, Type interceptingType)
            {
                ParameterInfo[] originalParams = originalConstructor.GetParameters();

                return interceptingType.GetConstructor(originalParams.Select(pi => pi.ParameterType).ToArray());
            }

            public static void SetPolicyForInterceptingType<TContext>(ref TContext context, Type interceptingType) 
                where TContext : IBuilderContext
            {
                var currentSelectorPolicy =
                    (IConstructorSelectorPolicy)context.Policies.GetOrDefault(typeof(IConstructorSelectorPolicy),
                                                                              context.OriginalBuildKey);
                if (!(currentSelectorPolicy is DerivedTypeConstructorSelectorPolicySelectorPolicy currentDerivedTypeSelectorPolicy))
                {
                    context.Registration.Set(typeof(IConstructorSelectorPolicy),
                                                  new DerivedTypeConstructorSelectorPolicySelectorPolicy(
                                                      interceptingType, currentSelectorPolicy));
                }
                else if (currentDerivedTypeSelectorPolicy._interceptingType != interceptingType)
                {
                    context.Registration.Set(typeof(IConstructorSelectorPolicy),
                                                  new DerivedTypeConstructorSelectorPolicySelectorPolicy(
                                                      interceptingType,
                                                      currentDerivedTypeSelectorPolicy.OriginalConstructorSelectorPolicySelectorPolicy));
                }
            }

            public object SelectConstructor<TContext>(ref TContext context) where TContext : IBuildContext
            {
                var originalConstructor = (ConstructorInfo)OriginalConstructorSelectorPolicySelectorPolicy.SelectConstructor(ref context);

                return FindNewConstructor(originalConstructor, _interceptingType);
            }
        }

        #endregion
    }
}
