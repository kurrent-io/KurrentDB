// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Pipelines;

/// <summary>
/// One typed processing stage. Stages compose with
/// <see cref="Step.Then{TIn,TMid,TOut}(IStep{TIn,TMid},IStep{TMid,TOut})"/>; the input/output
/// types enforce stage ordering at compile time.
/// </summary>
public interface IStep<in TIn, TOut> {
    ValueTask<TOut> Execute(TIn input, CancellationToken ct);
}

public static class Step {
    public static IStep<TIn, TOut> Then<TIn, TMid, TOut>(this IStep<TIn, TMid> first, IStep<TMid, TOut> second) =>
        new Composed<TIn, TMid, TOut>(first, second);

    /// <summary>
    /// Inline stage — lets a caller insert a lambda or method group into a chain without
    /// implementing <see cref="IStep{TIn,TOut}"/>.
    /// </summary>
    public static IStep<TIn, TOut> Then<TIn, TMid, TOut>(this IStep<TIn, TMid> first, Func<TMid, CancellationToken, ValueTask<TOut>> second) =>
        new Composed<TIn, TMid, TOut>(first, new LambdaStep<TMid, TOut>(second));

    sealed class Composed<TIn, TMid, TOut>(IStep<TIn, TMid> first, IStep<TMid, TOut> second) : IStep<TIn, TOut> {
        public async ValueTask<TOut> Execute(TIn input, CancellationToken ct) =>
            await second.Execute(await first.Execute(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    sealed class LambdaStep<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> run) : IStep<TIn, TOut> {
        public ValueTask<TOut> Execute(TIn input, CancellationToken ct) => run(input, ct);
    }
}
