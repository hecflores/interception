﻿using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Builder;
using Unity.Interception.InterceptionBehaviors;
using Unity.Interception.Interceptors;
using Unity.Policy;
using Unity.Storage;
using Unity.Strategies;

namespace Unity.Interception.ContainerIntegration.ObjectBuilder
{
    /// <summary>
    /// A <see cref="BuilderStrategy"/> that intercepts objects
    /// in the build chain by creating a proxy object.
    /// </summary>
    public class InstanceInterceptionStrategy : BuilderStrategy
    {
        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <param name="context">Context of the build operation.</param>
        /// <param name="pre"></param>
        public override void PostBuildUp<TContext>(ref TContext context)
        {
            // If it's already been intercepted, don't do it again.
            if (context.Existing is IInterceptingProxy)
            {
                return;
            }

            IInstanceInterceptionPolicy interceptionPolicy =
                FindInterceptionPolicy<IInstanceInterceptionPolicy>(context, true);
            if (interceptionPolicy == null)
            {
                return;
            }

            var interceptor = interceptionPolicy.GetInterceptor(context);

            IInterceptionBehaviorsPolicy interceptionBehaviorsPolicy =
                FindInterceptionPolicy<IInterceptionBehaviorsPolicy>(context, true);
            if (interceptionBehaviorsPolicy == null)
            {
                return;
            }

            IAdditionalInterfacesPolicy additionalInterfacesPolicy =
                FindInterceptionPolicy<IAdditionalInterfacesPolicy>(context, false);
            IEnumerable<Type> additionalInterfaces =
                additionalInterfacesPolicy != null ? additionalInterfacesPolicy.AdditionalInterfaces : Type.EmptyTypes;

            Type typeToIntercept = context.OriginalBuildKey.Type;
            Type implementationType = context.Existing.GetType();

            IInterceptionBehavior[] interceptionBehaviors =
                interceptionBehaviorsPolicy.GetEffectiveBehaviors(
                    context, interceptor, typeToIntercept, implementationType)
                .ToArray();

            if (interceptionBehaviors.Length > 0)
            {
                context.Existing =
                    Intercept.ThroughProxyWithAdditionalInterfaces(
                        typeToIntercept,
                        context.Existing,
                        interceptor,
                        interceptionBehaviors,
                        additionalInterfaces);
            }
        }

        private static T FindInterceptionPolicy<T>(IBuilderContext context, bool probeOriginalKey)
            where T : class, IBuilderPolicy
        {
            // First, try for an original build key
            var policy = (T)context.Policies.GetOrDefault(typeof(T), context.OriginalBuildKey) ??
                         (T)context.Policies.GetOrDefault(typeof(T), context.OriginalBuildKey.Type);

            if (policy != null)
            {
                return policy;
            }

            if (!probeOriginalKey)
            {
                return null;
            }

            // Next, try the build type
            policy = (T)context.Policies.GetOrDefault(typeof(T), context.BuildKey) ??
                     (T)context.Policies.GetOrDefault(typeof(T), context.BuildKey.Type);

            return policy;
        }
    }
}
