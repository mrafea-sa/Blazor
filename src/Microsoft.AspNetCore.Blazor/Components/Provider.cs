// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Blazor.Rendering;
using Microsoft.AspNetCore.Blazor.RenderTree;
using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Blazor.Components
{
    /// <summary>
    /// A component that provides one or more parameter values to all descendant components.
    /// </summary>
    public class Provider<T> : ProviderBase, IComponent
    {
        private RenderHandle _renderHandle;
        private HashSet<ComponentState> _subscribers; // Lazily instantiated

        /// <summary>
        /// The content to which the value should be provided.
        /// </summary>
        [Parameter] private RenderFragment ChildContent { get; set; }

        /// <summary>
        /// The value to be provided.
        /// </summary>
        [Parameter] private T Value { get; set; }

        internal override object CurrentValue => Value;

        /// <summary>
        /// Optionally gives a name to the provided value. Descendant components
        /// will be able to receive the value by specifying this name.
        ///
        /// If no name is specified, then descendant components will receive the
        /// value based the type of value they are requesting.
        /// </summary>
        [Parameter] private string Name { get; set; }

        /// <inheritdoc />
        public void Init(RenderHandle renderHandle)
        {
            _renderHandle = renderHandle;
        }

        /// <inheritdoc />
        public void SetParameters(ParameterCollection parameters)
        {
            // Implementing the parameter binding manually, instead of just calling
            // parameters.AssignToProperties(this), is just a very slight perf optimization
            // and makes it simpler impose rules about the params being required or not.

            var hasSuppliedValue = false;
            var previousValue = Value;
            Value = default;
            ChildContent = null;
            Name = null;

            foreach (var parameter in parameters)
            {
                if (parameter.Name.Equals(nameof(Value), StringComparison.OrdinalIgnoreCase))
                {
                    Value = (T)parameter.Value;
                    hasSuppliedValue = true;
                }
                else if (parameter.Name.Equals(nameof(ChildContent), StringComparison.OrdinalIgnoreCase))
                {
                    ChildContent = (RenderFragment)parameter.Value;
                }
                else if (parameter.Name.Equals(nameof(Name), StringComparison.OrdinalIgnoreCase))
                {
                    Name = (string)parameter.Value;
                    if (string.IsNullOrEmpty(Name))
                    {
                        throw new ArgumentException($"The parameter '{nameof(Name)}' for component '{nameof(Provider<T>)}' does not allow null or empty values.");
                    }
                }
                else
                {
                    throw new ArgumentException($"The component '{nameof(Provider<T>)}' does not accept a parameter with the name '{parameter.Name}'.");
                }
            }

            // It's OK for the value to be null, but some "Value" param must be suppled
            // because it serves no useful purpose to have a <Provider> otherwise.
            if (!hasSuppliedValue)
            {
                throw new ArgumentException($"Missing required parameter '{nameof(Value)}' for component '{nameof(Parameter)}'.");
            }

            // Rendering is most efficient when things are queued from rootmost to leafmost.
            // Given a components A (parent) -> B (child), you want them to be queued in order
            // [A, B] because if you had [B, A], then the render for A might change B's params
            // making it render again, so you'd render [B, A, B], which is wasteful.
            // At some point we might consider making the render queue actually enforce this
            // ordering during insertion.
            //
            // For the Provider component, this observation is why it's important to render
            // ourself before notifying subscribers (which can be grandchildren or deeper).
            // If we rerendered subscribers first, then our own subsequent render might cause an
            // further update that makes those nested subscribers get rendered twice.
            _renderHandle.Render(Render);

            if (_subscribers != null && ChangeDetection.MayHaveChanged(previousValue, Value))
            {
                NotifySubscribers();
            }
        }

        internal override bool CanSupplyValue(Type requestedType, string requestedName)
        {
            if (!requestedType.IsAssignableFrom(typeof(T)))
            {
                return false;
            }

            return (requestedName == null && Name == null) // Match on type alone
                || string.Equals(requestedName, Name, StringComparison.Ordinal); // Also match on name
        }

        internal override void Subscribe(ComponentState subscriber)
        {
            if (_subscribers == null)
            {
                 _subscribers = new HashSet<ComponentState>();
            }

            _subscribers.Add(subscriber);
        }

        internal override void Unsubscribe(ComponentState subscriber)
        {
            _subscribers.Remove(subscriber);
        }

        private void NotifySubscribers()
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.NotifyTreeParameterChanged();
            }
        }

        private void Render(RenderTreeBuilder builder)
        {
            builder.AddContent(0, ChildContent);
        }
    }
}
