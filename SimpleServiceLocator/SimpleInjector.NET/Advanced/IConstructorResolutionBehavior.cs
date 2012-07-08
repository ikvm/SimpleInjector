﻿#region Copyright (c) 2010 S. van Deursen
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (C) 2010 S. van Deursen
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
    using System.Reflection;

    /// <summary>
    /// Defines the container's behavior for finding a suitable constructor for the creation of a type.
    /// </summary>
    public interface IConstructorResolutionBehavior
    {
        /// <summary>
        /// Gets the given <paramref name="implementationType"/>'s constructor that can be used by the 
        /// container to create that instance.
        /// </summary>
        /// <param name="serviceType">Type of the abstraction that is requested.</param>
        /// <param name="implementationType">Type of the implementation to find a suitable constructor for.</param>
        /// <returns>
        /// The <see cref="ConstructorInfo"/>.
        /// </returns>
        /// <exception cref="ActivationException">Thrown when no suitable constructor could be found.</exception>
        ConstructorInfo GetConstructor(Type serviceType, Type implementationType);

        //// NOTE: The serviceType parameter is not used in the default implementation, but can be used by
        //// alternative implementations to generate a proxy type based on the service type and return a
        //// constructor of that proxy instead of returning a constructor of the implementationType.
    }
}