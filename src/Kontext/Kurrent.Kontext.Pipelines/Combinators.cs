// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Pipelines;

/// <summary>
/// The two fan-in shapes the memory pipelines need beside plain chaining: parallel branches
/// folded into one result, and a sequential cascade that can stop early. Branches are
/// alternatives over the same input — unlike <see cref="Step.Then{TIn,TMid,TOut}(IStep{TIn,TMid},IStep{TMid,TOut})"/>,
/// where each stage transforms the previous stage's output.
/// </summary>
public static class Steps {
    /// <summary>Lifts a lambda or method group into a step — the head of an ad-hoc chain.</summary>
    public static IStep<TIn, TOut> Lambda<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> run) =>
        new LambdaStep<TIn, TOut>(run);

    /// <summary>
    /// Runs every branch in parallel over the same input and folds the results into one output.
    /// A failing branch fails the whole step — fan-in is for legs that are each load-bearing.
    /// </summary>
    public static IStep<TIn, TOut> FanIn<TIn, TMid, TOut>(
        IReadOnlyList<IStep<TIn, TMid>> branches,
        Func<IReadOnlyList<TMid>, TIn, TOut> fold
    ) {
        return branches.Count > 0
            ? new FanInStep<TIn, TMid, TOut>(branches, fold)
            : throw new ArgumentException(@"Fan-in needs at least one branch.", nameof(branches));
    }

    /// <summary>
    /// Runs the branches sequentially over the same input, stopping after the first result
    /// <paramref name="stop"/> accepts, then folds every collected result into one output.
    /// <para><paramref name="skipOnError"/> decides a failing branch's fate: return true to drop
    /// it and continue (log there — the callback receives the branch index), false or absent to
    /// rethrow. Cancellation always propagates.</para>
    /// </summary>
    public static IStep<TIn, TOut> Cascade<TIn, TMid, TOut>(
        IReadOnlyList<IStep<TIn, TMid>> branches,
        Func<TMid, bool> stop,
        Func<IReadOnlyList<TMid>, TIn, TOut> fold,
        Func<Exception, int, bool>? skipOnError = null
    ) {
        return branches.Count > 0
            ? new CascadeStep<TIn, TMid, TOut>(branches, stop, fold, skipOnError)
            : throw new ArgumentException(@"A cascade needs at least one branch.", nameof(branches));
    }

    sealed class LambdaStep<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> run) : IStep<TIn, TOut> {
        public ValueTask<TOut> Execute(TIn input, CancellationToken ct) => run(input, ct);
    }

    sealed class FanInStep<TIn, TMid, TOut>(
        IReadOnlyList<IStep<TIn, TMid>> branches,
        Func<IReadOnlyList<TMid>, TIn, TOut> fold
    ) : IStep<TIn, TOut> {
        public async ValueTask<TOut> Execute(TIn input, CancellationToken ct) {
            var results = await Task
                .WhenAll(branches.Select(branch => branch.Execute(input, ct).AsTask()))
                .ConfigureAwait(false);

            return fold(results, input);
        }
    }

    sealed class CascadeStep<TIn, TMid, TOut>(
        IReadOnlyList<IStep<TIn, TMid>> branches,
        Func<TMid, bool> stop,
        Func<IReadOnlyList<TMid>, TIn, TOut> fold,
        Func<Exception, int, bool>? skipOnError
    ) : IStep<TIn, TOut> {
        public async ValueTask<TOut> Execute(TIn input, CancellationToken ct) {
            var results = new List<TMid>(branches.Count);

            for (var index = 0; index < branches.Count; index++) {
                ct.ThrowIfCancellationRequested();

                TMid result;
                try {
                    result = await branches[index].Execute(input, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    throw;
                }
                catch (Exception ex) when (skipOnError?.Invoke(ex, index) == true) {
                    continue;
                }

                results.Add(result);

                if (stop(result))
                    break;
            }

            return fold(results, input);
        }
    }
}
