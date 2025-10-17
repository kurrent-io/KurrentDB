// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Testing;

/// <summary>
/// A simple async disposable struct that can hold multiple async disposal actions.
/// Useful for composing multiple async disposable resources into a single disposable unit.
/// </summary>
[PublicAPI]
public readonly record struct Disposable(params Func<ValueTask>[] Actions) : IAsyncDisposable {
    static int _disposed;

    public async ValueTask DisposeAsync() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
            // Dispose in reverse order using modern iteration patterns
            foreach (var disposable in Actions.AsEnumerable().Reverse())
                await disposable().ConfigureAwait(false);
        }
    }

    public static DisposableBuilder Create => new DisposableBuilder();

    public static Disposable From(Func<ValueTask> action) =>
        Create.With(action).Build();

    /// <summary>
    /// Builder for composing async disposable resources with a fluent API
    /// </summary>
    public sealed class DisposableBuilder {
        readonly List<Func<ValueTask>> _actions = [];

        internal DisposableBuilder() { }

        public DisposableBuilder With(Func<ValueTask> action) {
            _actions.Add(action);
            return this;
        }

        public DisposableBuilder With(IAsyncDisposable disposable) =>
            With(disposable.DisposeAsync);

        public DisposableBuilder With(Func<Task> action) {
            _actions.Add(async () => await action().ConfigureAwait(false));
            return this;
        }

        public DisposableBuilder With(Action action) =>
            With(() => { action(); return ValueTask.CompletedTask; });

        public DisposableBuilder With(IDisposable disposable) =>
            With(disposable.Dispose);

        public DisposableBuilder With(object obj) =>
            obj switch {
                IAsyncDisposable disposable => With(disposable),
                IDisposable disposable      => With(disposable),
                _                           => throw new ArgumentException($"Object of type {obj.GetType().Name} is not disposable", nameof(obj))
            };

        public Disposable Build() => new(_actions.ToArray());

        public static implicit operator Disposable(DisposableBuilder builder) => builder.Build();
    }
}
