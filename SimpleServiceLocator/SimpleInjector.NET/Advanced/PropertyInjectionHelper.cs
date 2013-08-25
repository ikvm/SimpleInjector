﻿#region Copyright (c) 2013 S. van Deursen
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (C) 2013 S. van Deursen
 * 
 * To contact me, please visit my blog at http://www.cuttingedge.it/blogs/steven/ or mail to steven at 
 * cuttingedge.it.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
 * associated documentation files (the "Software"), to deal in the Software without restriction, including 
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the 
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial 
 * portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO 
 * EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion

namespace SimpleInjector.Advanced
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Security;
    using System.Threading;

    internal sealed class PropertyInjectionHelper
    {
        private const int MaximumNumberOfFuncArguments = 16;
        private const int MaximumNumberOfPropertiesPerDelegate = MaximumNumberOfFuncArguments - 1;

        private static readonly ReadOnlyCollection<Type> FuncTypes = new ReadOnlyCollection<Type>(new Type[]
            {
                null,
                typeof(Func<>),
                typeof(Func<,>),
                typeof(Func<,,>),
                typeof(Func<,,,>),
                typeof(Func<,,,,>),
                typeof(Func<,,,,,>),
                typeof(Func<,,,,,,>),
                typeof(Func<,,,,,,,>),
                typeof(Func<,,,,,,,,>),
                typeof(Func<,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,,,,,>),
            });

#if !SILVERLIGHT
        private static long injectorClassCounter;
#endif

        private readonly Container container;
        private readonly Type implementationType;

        internal PropertyInjectionHelper(Container container, Type implementationType)
        {
            this.container = container;
            this.implementationType = implementationType;
        }

        internal static Expression BuildPropertyInjectionExpression(Container container,
            Type implementationType, PropertyInfo[] properties, Expression expressionToWrap)
        {
            var helper = new PropertyInjectionHelper(container, implementationType);

            return helper.BuildPropertyInjectionExpression(expressionToWrap, properties);
        }

        internal static PropertyInfo[] GetCandidateInjectionPropertiesFor(Type implementationType)
        {
            var all = BindingFlags.FlattenHierarchy | 
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.NonPublic | BindingFlags.Public;

            return implementationType.GetProperties(all);
        }

        internal static void VerifyProperties(PropertyInfo[] properties)
        {
            foreach (var property in properties)
            {
                VerifyProperty(property);
            }
        }
        
        private Delegate BuildPropertyInjectionDelegate(PropertyInfo[] properties)
        {
            try
            {
                return this.BuildPropertyInjectionDelegateInternal(properties);
            }
            catch (MemberAccessException ex)
            {
                // This happens when the user tries to resolve an internal type inside a (Silverlight) sandbox.
                throw new ActivationException(
                    StringResources.UnableToInjectPropertiesDueToSecurityConfiguration(this.implementationType,
                        ex),
                    ex);
            }
        }

        private Delegate BuildPropertyInjectionDelegateInternal(PropertyInfo[] properties)
        {
            var targetParameter = Expression.Parameter(this.implementationType, this.implementationType.Name);

            var dependencyParameters = (
                from property in properties
                select Expression.Parameter(property.PropertyType, property.Name))
                .ToArray();

            var propertyInjectionExpressions =
                this.BuildPropertyInjectionExpressions(targetParameter, properties, dependencyParameters);

            Type funcType = GetFuncType(properties, this.implementationType);

            var parameters = dependencyParameters.Concat(new[] { targetParameter });

            var lambda = Expression.Lambda(
                funcType,
                Expression.Block(this.implementationType, propertyInjectionExpressions),
                parameters);

            return this.CompilePropertyInjectorLambda(lambda);
        }

        private List<Expression> BuildPropertyInjectionExpressions(ParameterExpression targetParameter, 
            PropertyInfo[] properties, 
            ParameterExpression[] dependencyParameters)
        {
            var blockExpressions = (
                from pair in properties.Zip(dependencyParameters, (prop, param) => new { prop, param })
                select Expression.Assign(Expression.Property(targetParameter, pair.prop), pair.param))
                .Cast<Expression>()
                .ToList();

            var returnTarget = Expression.Label(this.implementationType);

            blockExpressions.Add(Expression.Return(returnTarget, targetParameter, this.implementationType));
            blockExpressions.Add(Expression.Label(returnTarget, Expression.Constant(null, this.implementationType)));
            
            return blockExpressions;
        }

        private static void VerifyProperty(PropertyInfo property)
        {
            MethodInfo setMethod = property.GetSetMethod(nonPublic: true);

            if (setMethod == null)
            {
                throw new ActivationException(StringResources.PropertyHasNoSetter(property));
            }

            if (setMethod.IsStatic)
            {
                throw new ActivationException(StringResources.PropertyIsStatic(property));
            }
        }

        private Expression BuildPropertyInjectionExpression(Expression expression, PropertyInfo[] properties)
        {
            // Build up an expression like this:
            // () => func1(Dep1, Dep2, Dep3, func2(Dep4, Dep5, Dep6), func3(Dep7, new TargetType(...))))
            // () => func1(func2(func3(new TargetType(...), Dep7), Dep4, Dep5, Dep6), Dep1, Dep2, Dep3)
            if (properties.Length > MaximumNumberOfPropertiesPerDelegate)
            {
                // Expression becomes: Func<Prop8, Prop9, ... , PropN, TargetType>
                var restProperties = properties.Skip(MaximumNumberOfPropertiesPerDelegate).ToArray();
                expression = this.BuildPropertyInjectionExpression(expression, restProperties);

                // Properties becomes { Prop1, Prop2, ..., Prop7 }.
                properties = properties.Take(MaximumNumberOfPropertiesPerDelegate).ToArray();
            }

            Expression[] dependencyExpressions = this.GetPropertyExpressions(properties);

            // var arguments = new[] { expression }.Concat(dependencyExpressions);
            var arguments = dependencyExpressions.Concat(new[] { expression });

            Delegate propertyInjectionDelegate = this.BuildPropertyInjectionDelegate(properties);

            return Expression.Invoke(Expression.Constant(propertyInjectionDelegate), arguments);
        }

        private Expression[] GetPropertyExpressions(PropertyInfo[] properties)
        {
            return (
                from property in properties
                select this.GetPropertyExpression(property))
                .ToArray();
        }

        private Expression GetPropertyExpression(PropertyInfo property)
        {
            InstanceProducer registration;

            try
            {
                registration = this.container.GetRegistration(property.PropertyType, throwOnFailure: true);
            }
            catch (Exception ex)
            {
                // Throw a more expressive exception.
                throw new ActivationException(StringResources.NoRegistrationForPropertyFound(property, ex), ex);
            }

            return registration.BuildExpression();
        }

        private static Type GetFuncType(PropertyInfo[] properties, Type injecteeType)
        {
            var genericTypeArguments = new List<Type>();

            genericTypeArguments.AddRange(from property in properties select property.PropertyType);

            genericTypeArguments.Add(injecteeType);

            // Return type is TResult. This is always the last generic type.
            genericTypeArguments.Add(injecteeType);

            int numberOfInputArguments = genericTypeArguments.Count;

            Type openGenericFuncType = FuncTypes[numberOfInputArguments];

            return openGenericFuncType.MakeGenericType(genericTypeArguments.ToArray());
        }
        
        private Delegate CompilePropertyInjectorLambda(LambdaExpression expression)
        {
#if !SILVERLIGHT
            if (this.container.Options.EnableDynamicAssemblyCompilation && 
                !Helpers.ExpressionNeedsAccessToInternals(expression))
            {
                return this.CompileLambdaInDynamicAssemblyWithFallback(expression);
            }
#endif
            return expression.Compile();
        }

#if !SILVERLIGHT
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes",
            Justification = "Not all delegates can be JITted. We fallback to the slower expression.Compile " +
                            "in that case.")]
        private Delegate CompileLambdaInDynamicAssemblyWithFallback(LambdaExpression expression)
        {
            try
            {
                var @delegate = Helpers.CompileLambdaInDynamicAssembly(this.container, expression,
                    "DynamicPropertyInjector" + GetNextInjectorClassId(), 
                    "InjectProperties");

                // Test the creation. Since we're using a dynamically created assembly, we can't create every
                // delegate we can create using expression.Compile(), so we need to test this. We need to 
                // store the created instance because we are not allowed to ditch that instance.
                JitCompileDelegate(@delegate);

                return @delegate;
            }
            catch
            {
                // The fallback
                return expression.Compile();
            }
        }

        [SecuritySafeCritical]
        private static void JitCompileDelegate(Delegate @delegate)
        {
            RuntimeHelpers.PrepareDelegate(@delegate);
        }

        private static long GetNextInjectorClassId()
        {
            return Interlocked.Increment(ref injectorClassCounter);
        }
#endif
    }
}