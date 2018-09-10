﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Build.Delegates;
using Unity.Build.Selection;
using Unity.Builder;
using Unity.Builder.Strategy;
using Unity.Interception.InterceptionBehaviors;
using Unity.Interception.Interceptors;
using Unity.Policy;

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
        public override void PreBuildUp(IBuilderContext context)
        {
            if (context.Existing != null)
            {
                return;
            }

            Type typeToBuild = context.BuildKey.Type;

            var interceptionPolicy = FindInterceptionPolicy<ITypeInterceptionPolicy>(context);
            if (interceptionPolicy == null)
            {
                return;
            }

            var interceptor = interceptionPolicy.GetInterceptor(context);
            if (!interceptor.CanIntercept(typeToBuild))
            {
                return;
            }

            var interceptionBehaviorsPolicy = FindInterceptionPolicy<IInterceptionBehaviorsPolicy>(context);

            IEnumerable<IInterceptionBehavior> interceptionBehaviors =
                interceptionBehaviorsPolicy == null
                    ?
                        Enumerable.Empty<IInterceptionBehavior>()
                    :
                        interceptionBehaviorsPolicy.GetEffectiveBehaviors(
                            context, interceptor, typeToBuild, typeToBuild)
                        .Where(ib => ib.WillExecute);

            IAdditionalInterfacesPolicy additionalInterfacesPolicy =
                FindInterceptionPolicy<IAdditionalInterfacesPolicy>(context);

            IEnumerable<Type> additionalInterfaces =
                additionalInterfacesPolicy != null ? additionalInterfacesPolicy.AdditionalInterfaces : Type.EmptyTypes;

            var enumerable = interceptionBehaviors as IInterceptionBehavior[] ?? interceptionBehaviors.ToArray();
            context.Registration.Set(typeof(EffectiveInterceptionBehaviorsPolicy), 
                new EffectiveInterceptionBehaviorsPolicy { Behaviors = enumerable });

            Type[] allAdditionalInterfaces =
                Intercept.GetAllAdditionalInterfaces(enumerable, additionalInterfaces);

            Type interceptingType =
                interceptor.CreateProxyType(typeToBuild, allAdditionalInterfaces);

            DerivedTypeConstructorSelectorPolicy.SetPolicyForInterceptingType(context, interceptingType);
        }

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <remarks>In this class, PostBuildUp checks to see if the object was proxyable,
        /// and if it was, wires up the handlers.</remarks>
        /// <param name="context">Context of the build operation.</param>
        public override void PostBuildUp(IBuilderContext context)
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


        private static TPolicy FindInterceptionPolicy<TPolicy>(IBuilderContext context)
            where TPolicy : class, IBuilderPolicy
        {
            return (TPolicy)context.Policies.GetOrDefault(typeof(TPolicy), context.OriginalBuildKey) ??
                   (TPolicy)context.Policies.GetOrDefault(typeof(TPolicy), context.OriginalBuildKey.Type);
        }

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

        private class DerivedTypeConstructorSelectorPolicy
        {
            internal readonly Type _interceptingType;
            internal readonly SelectCtorDelegate _originalConstructorSelectorPolicy;

            internal DerivedTypeConstructorSelectorPolicy(Type interceptingType,
                SelectCtorDelegate originalConstructorSelectorPolicy)
            {
                _interceptingType = interceptingType;
                _originalConstructorSelectorPolicy = originalConstructorSelectorPolicy;
            }

            public virtual SelectCtorDelegate SelectDelegate => context =>
            {
                SelectedConstructor originalConstructor = (SelectedConstructor)_originalConstructorSelectorPolicy(context);

                return FindNewConstructor(originalConstructor, _interceptingType);
            };

            private static SelectedConstructor FindNewConstructor(SelectedConstructor originalConstructor, Type interceptingType)
            {
                ParameterInfo[] originalParams = originalConstructor.Constructor.GetParameters();

                ConstructorInfo newConstructorInfo =
                    interceptingType.GetConstructor(originalParams.Select(pi => pi.ParameterType).ToArray());

                SelectedConstructor newConstructor = new SelectedConstructor(newConstructorInfo);

                foreach (ResolverDelegate resolver in originalConstructor.GetParameterResolvers())
                {
                    newConstructor.AddParameterResolver(resolver);
                }

                return newConstructor;
            }

            public static void SetPolicyForInterceptingType(IBuilderContext context, Type interceptingType)
            {
                var currentSelectorPolicy = (SelectCtorDelegate)context.Policies.GetOrDefault(typeof(SelectCtorDelegate),
                                                                       context.OriginalBuildKey);
                if (!(currentSelectorPolicy.Target is DerivedTypeConstructorSelectorPolicy currentDerivedTypeSelectorPolicy))
                {
                    context.Registration.Set(typeof(SelectCtorDelegate),
                                             new DerivedTypeConstructorSelectorPolicy(interceptingType, currentSelectorPolicy).SelectDelegate);
                }
                else if (currentDerivedTypeSelectorPolicy._interceptingType != interceptingType)
                {
                    context.Registration.Set(typeof(SelectCtorDelegate),
                                                  new DerivedTypeConstructorSelectorPolicy(interceptingType, 
                                                      currentDerivedTypeSelectorPolicy._originalConstructorSelectorPolicy).SelectDelegate);
                }
            }
        }

        #endregion
    }
}
