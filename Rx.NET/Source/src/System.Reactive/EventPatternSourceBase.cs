﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information. 

using System.Collections.Generic;

namespace System.Reactive
{
    /// <summary>
    /// Base class for classes that expose an observable sequence as a well-known event pattern (sender, event arguments).
    /// Contains functionality to maintain a map of event handler delegates to observable sequence subscriptions. Subclasses
    /// should only add an event with custom add and remove methods calling into the base class's operations.
    /// </summary>
    /// <typeparam name="TSender">The type of the sender that raises the event.</typeparam>
    /// <typeparam name="TEventArgs">The type of the event data generated by the event.</typeparam>
    public abstract class EventPatternSourceBase<TSender, TEventArgs>
    {
        private sealed class Observer : ObserverBase<EventPattern<TSender, TEventArgs>>, ISafeObserver<EventPattern<TSender, TEventArgs>>
        {
            private bool _isDone;
            private bool _isAdded;
            private readonly Delegate _handler;
            private readonly object _gate = new object();
            private readonly Action<TSender, TEventArgs> _invoke;
            private readonly EventPatternSourceBase<TSender, TEventArgs> _sourceBase;

            public Observer(EventPatternSourceBase<TSender, TEventArgs> sourceBase, Delegate handler, Action<TSender, TEventArgs> invoke)
            {
                _handler = handler;
                _invoke = invoke;
                _sourceBase = sourceBase;
            }

            protected override void OnNextCore(EventPattern<TSender, TEventArgs> value)
            {
                _sourceBase._invokeHandler(_invoke, value);
            }

            protected override void OnErrorCore(Exception error)
            {
                Remove();
                error.Throw();
            }

            protected override void OnCompletedCore()
            {
                Remove();
            }

            private void Remove()
            {
                lock (_gate)
                {
                    if (_isAdded)
                    {
                        _sourceBase.Remove(_handler);
                    }
                    else
                    {
                        _isDone = true;
                    }
                }
            }

            public void SetResource(IDisposable resource)
            {
                lock (_gate)
                {
                    if (!_isDone)
                    {
                        _sourceBase.Add(_handler, resource);
                        _isAdded = true;
                    }
                }
            }
        }

        private readonly IObservable<EventPattern<TSender, TEventArgs>> _source;
        private readonly Dictionary<Delegate, Stack<IDisposable>> _subscriptions;
        private readonly Action<Action<TSender, TEventArgs>, /*object,*/ EventPattern<TSender, TEventArgs>> _invokeHandler;

        /// <summary>
        /// Creates a new event pattern source.
        /// </summary>
        /// <param name="source">Source sequence to expose as an event.</param>
        /// <param name="invokeHandler">Delegate used to invoke the event for each element of the sequence.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="invokeHandler"/> is <c>null</c>.</exception>
        protected EventPatternSourceBase(IObservable<EventPattern<TSender, TEventArgs>> source, Action<Action<TSender, TEventArgs>, /*object,*/ EventPattern<TSender, TEventArgs>> invokeHandler)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _invokeHandler = invokeHandler ?? throw new ArgumentNullException(nameof(invokeHandler));
            _subscriptions = new Dictionary<Delegate, Stack<IDisposable>>();
        }

        /// <summary>
        /// Adds the specified event handler, causing a subscription to the underlying source.
        /// </summary>
        /// <param name="handler">Event handler to add. The same delegate should be passed to the <see cref="Remove(Delegate)"/> operation in order to remove the event handler.</param>
        /// <param name="invoke">Invocation delegate to raise the event in the derived class.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> or <paramref name="invoke"/> is <c>null</c>.</exception>
        protected void Add(Delegate handler, Action<TSender, TEventArgs> invoke)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (invoke == null)
            {
                throw new ArgumentNullException(nameof(invoke));
            }

            var observer = new Observer(this, handler, invoke);
            //
            // [OK] Use of unsafe Subscribe: non-pretentious wrapper of an observable in an event; exceptions can occur during +=.
            //
            observer.SetResource(_source.Subscribe(observer));
        }

        private void Add(Delegate handler, IDisposable disposable)
        {
            lock (_subscriptions)
            {
                if (!_subscriptions.TryGetValue(handler, out var l))
                {
                    _subscriptions[handler] = l = new Stack<IDisposable>();
                }

                l.Push(disposable);
            }
        }

        /// <summary>
        /// Removes the specified event handler, causing a disposal of the corresponding subscription to the underlying source that was created during the <see cref="Add(Delegate, Action{TSender, TEventArgs})"/> operation.
        /// </summary>
        /// <param name="handler">Event handler to remove. This should be the same delegate as one that was passed to the <see cref="Add(Delegate, Action{TSender, TEventArgs})"/> operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        protected void Remove(Delegate handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var d = default(IDisposable);

            lock (_subscriptions)
            {
                if (_subscriptions.TryGetValue(handler, out var l))
                {
                    d = l.Pop();

                    if (l.Count == 0)
                    {
                        _subscriptions.Remove(handler);
                    }
                }
            }

            d?.Dispose();
        }
    }
}
