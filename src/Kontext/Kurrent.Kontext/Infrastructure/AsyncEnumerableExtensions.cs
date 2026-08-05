using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kurrent.Surge.Client;

/// <summary>
/// Classification result for batching: either add to batch or flush and yield.
/// </summary>
/// <typeparam name="TBatch">Type of items that can be batched.</typeparam>
/// <typeparam name="TPassThrough">Type of items that pass through (flush triggers).</typeparam>
readonly struct BatchAction<TBatch, TPassThrough> {
    readonly TBatch?       _batchItem;
    readonly TPassThrough? _passThrough;

    BatchAction(bool isBatchable, TBatch? batchItem, TPassThrough? passThrough) {
        IsBatchable  = isBatchable;
        _batchItem   = batchItem;
        _passThrough = passThrough;
    }

    /// <summary>True if this item should be added to the current batch.</summary>
    public bool IsBatchable { get; }

    /// <summary>The item to add to the batch. Only valid when <see cref="IsBatchable"/> is true.</summary>
    public TBatch BatchItem => _batchItem!;

    /// <summary>The item to yield after flushing. Only valid when <see cref="IsBatchable"/> is false.</summary>
    public TPassThrough PassThrough => _passThrough!;

    /// <summary>Add this item to the current batch.</summary>
    public static BatchAction<TBatch, TPassThrough> Batch(TBatch item) => new(true, item, default);

    /// <summary>Flush the current batch, then yield this item.</summary>
    public static BatchAction<TBatch, TPassThrough> FlushThenYield(TPassThrough item) => new(false, default, item);
}

static class AsyncEnumerableExtensions {
    /// <summary>
    /// Reads data from the async enumerable in batches of a specified size up to a given window of time.
    /// Uses ArrayPool internally for efficient batch building, then creates a List via CollectionsMarshal.
    /// </summary>
    /// <param name="source">The async enumerable to read data from.</param>
    /// <param name="maxBatchSize">The maximum number of items in a single batch.</param>
    /// <param name="batchWindow">The maximum duration to wait while gathering a batch.</param>
    /// <param name="cancellationToken">A token to observe for cancellation of the batch processing.</param>
    /// <typeparam name="T">The type of data in the enumerable.</typeparam>
    /// <remarks>
    /// Uses the "park and resume" pattern to safely handle timeouts without violating the async enumerable
    /// serialization rule. When a window expires while MoveNextAsync is pending, the task is "parked" and
    /// resumed on the next batch iteration, ensuring disposal never happens mid-await.
    /// </remarks>
    public static async IAsyncEnumerable<List<T>> ReadBatches<T>(
        this IAsyncEnumerable<T> source,
        int maxBatchSize, TimeSpan batchWindow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
            var pool   = ArrayPool<T>.Shared;
            var buffer = pool.Rent(maxBatchSize);
            ValueTask<bool>? parkedTask = null;

            var enumerator = source.GetAsyncEnumerator(cancellationToken);

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    var count = 0;
                    var start = TimeProvider.System.GetTimestamp();

                    while (count < maxBatchSize) {
                        bool hasNext;

                        // Resume parked task OR start fresh
                        var moveNext = parkedTask ?? enumerator.MoveNextAsync();
                        parkedTask = null;

                        // Fast path: synchronous completion avoids task allocation
                        if (moveNext.IsCompletedSuccessfully) {
                            hasNext = moveNext.Result;
                        }
                        else {
                            // Slow path: async with remaining window
                            var remaining = batchWindow - TimeProvider.System.GetElapsedTime(start);

                            if (remaining <= TimeSpan.Zero) {
                                parkedTask = moveNext; // Park for next iteration
                                break;
                            }

                            // Use WhenAny instead of WaitAsync to avoid abandoning pending task
                            var moveNextTask = moveNext.AsTask();
                            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var delayTask = Task.Delay(remaining, delayCts.Token);

                            var completed = await Task.WhenAny(moveNextTask, delayTask).ConfigureAwait(false);

                            if (completed != moveNextTask) {
                                parkedTask = new(moveNextTask); // Timeout - park the Task wrapper (ValueTask is consumed by AsTask)
                                break;
                            }

                            // Cancel the delay task to avoid timer leak
                            await delayCts.CancelAsync().ConfigureAwait(false);

                            hasNext = await moveNextTask.ConfigureAwait(false);
                        }

                        if (!hasNext) {
                            // Source exhausted
                            if (count > 0)
                                yield return ToList(buffer, count);

                            yield break;
                        }

                        buffer[count++] = enumerator.Current;
                    }

                    if (count > 0)
                        yield return ToList(buffer, count);
                }
            }
            finally {
                // Safety net: await parked task before implicit DisposeAsync
                if (parkedTask.HasValue) {
                    try {
                        await parkedTask.Value.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        // Expected on cancellation
                    }
                }

                // Only clear if T can hold references (avoids clearing value types)
                pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

                // Manual disposal: ChannelReader.ReadAllAsync enumerator throws
                // NotSupportedException on DisposeAsync when interrupted mid-iteration.
                // Suppressing prevents it from replacing the original exception.
                try {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch (NotSupportedException) {
                    // Expected: async iterator disposal not supported in certain states
                }
            }

            yield break;

            // Creates a List from a buffer segment using CollectionsMarshal for efficient bulk copy.
            // Avoids List.Add() overhead by directly setting count and copying via Span.
            static List<T> ToList(T[] buffer, int count) {
                var list = new List<T>(count);
                CollectionsMarshal.SetCount(list, count);
                buffer.AsSpan(0, count).CopyTo(CollectionsMarshal.AsSpan(list));
                return list;
            }
        }

    /// <summary>
    /// Batches items from the source, yielding flush-trigger items immediately after flushing the pending batch.
    /// </summary>
    /// <param name="source">The async enumerable to read data from.</param>
    /// <param name="maxBatchSize">Maximum items per batch.</param>
    /// <param name="batchWindow">Maximum time to collect a batch.</param>
    /// <param name="classify">Classifies each item as batchable or flush-trigger.</param>
    /// <param name="batchToOutput">Converts batch array to output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The type of data in the enumerable.</typeparam>
    /// <typeparam name="TBatch">Type of items that can be batched.</typeparam>
    /// <typeparam name="TOutput">Output type (batch or pass-through).</typeparam>
    /// <returns>An async enumerable of batched outputs and pass-through items.</returns>
    /// <remarks>
    /// <para>
    /// Batches are yielded when: batch size is reached, window expires, source exhausted, or a flush-trigger arrives.
    /// </para>
    /// <para>
    /// Flush-trigger items cause pending batched items to be yielded first, then the trigger item itself.
    /// </para>
    /// <para>
    /// Uses the "park and resume" pattern to safely handle timeouts without violating the async enumerable
    /// serialization rule. When a window expires while MoveNextAsync is pending, the task is "parked" and
    /// resumed on the next batch iteration, ensuring disposal never happens mid-await.
    /// </para>
    /// </remarks>
    public static async IAsyncEnumerable<TOutput> ReadBatched<T, TBatch, TOutput>(
        this IAsyncEnumerable<T> source,
        int maxBatchSize,
        TimeSpan batchWindow,
        Func<T, BatchAction<TBatch, TOutput>> classify,
        Func<TBatch[], TOutput> batchToOutput,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
            var pool   = ArrayPool<TBatch>.Shared;
            var buffer = pool.Rent(maxBatchSize);
            ValueTask<bool>? parkedTask = null;

            var enumerator = source.GetAsyncEnumerator(cancellationToken);

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    var count = 0;
                    var start = TimeProvider.System.GetTimestamp();

                    while (count < maxBatchSize) {
                        bool hasNext;

                        // Resume parked task OR start fresh
                        var moveNext = parkedTask ?? enumerator.MoveNextAsync();
                        parkedTask = null;

                        // Fast path: synchronous completion avoids task allocation
                        if (moveNext.IsCompletedSuccessfully) {
                            hasNext = moveNext.Result;
                        }
                        else {
                            // Slow path: async with remaining window
                            var remaining = batchWindow - TimeProvider.System.GetElapsedTime(start);

                            if (remaining <= TimeSpan.Zero) {
                                parkedTask = moveNext; // Park for next iteration
                                break;
                            }

                            // Use WhenAny instead of WaitAsync to avoid abandoning pending task
                            var moveNextTask = moveNext.AsTask();
                            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var delayTask = Task.Delay(remaining, delayCts.Token);

                            var completed = await Task.WhenAny(moveNextTask, delayTask).ConfigureAwait(false);

                            if (completed != moveNextTask) {
                                parkedTask = new(moveNextTask); // Timeout - park the Task wrapper (ValueTask is consumed by AsTask)
                                break;
                            }

                            // Cancel the delay task to avoid timer leak
                            await delayCts.CancelAsync().ConfigureAwait(false);

                            hasNext = await moveNextTask.ConfigureAwait(false);
                        }

                        if (!hasNext) {
                            // Source exhausted - yield remaining batch
                            if (count > 0)
                                yield return batchToOutput(ToArray(buffer, count));

                            yield break;
                        }

                        // Classify the item
                        var action = classify(enumerator.Current);

                        if (action.IsBatchable) {
                            buffer[count++] = action.BatchItem;
                            continue;
                        }

                        // Flush trigger: yield pending batch first, then the trigger
                        if (count > 0) {
                            yield return batchToOutput(ToArray(buffer, count));
                            count = 0;
                        }

                        yield return action.PassThrough;
                    }

                    // Yield batch on size/timeout
                    if (count > 0)
                        yield return batchToOutput(ToArray(buffer, count));
                }
            }
            finally {
                // Safety net: await parked task before implicit DisposeAsync
                if (parkedTask.HasValue) {
                    try {
                        await parkedTask.Value.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        // Expected on cancellation
                    }
                }

                pool.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<TBatch>());

                // Manual disposal: ChannelReader.ReadAllAsync enumerator throws
                // NotSupportedException on DisposeAsync when interrupted mid-iteration.
                // Suppressing prevents it from replacing the original exception.
                try {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch (NotSupportedException) {
                    // Expected: async iterator disposal not supported in certain states
                }
            }

            yield break;

            static TBatch[] ToArray(TBatch[] buffer, int count) {
                var result = new TBatch[count];
                buffer.AsSpan(0, count).CopyTo(result);
                return result;
            }
        }
}
