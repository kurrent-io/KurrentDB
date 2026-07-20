using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.VectorData;

#pragma warning disable MEVD9000 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

/// <summary>
/// Wraps vector store operations so provider exceptions surface as <see cref="VectorStoreException"/> carrying
/// the store/collection/operation metadata.
/// </summary>
[ExcludeFromCodeCoverage]
static class VectorStoreErrorHandler {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<TResult> RunOperationAsync<TResult, TException>(
        VectorStoreMetadata metadata,
        string operationName,
        Func<Task<TResult>> operation
    )
        where TException : Exception =>
        RunOperationAsync<TResult, TException>(
            new VectorStoreCollectionMetadata {
                CollectionName        = null,
                VectorStoreName       = metadata.VectorStoreName,
                VectorStoreSystemName = metadata.VectorStoreSystemName
            },
            operationName,
            operation);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<TResult> RunOperationAsync<TResult, TException>(
        VectorStoreCollectionMetadata metadata,
        string operationName,
        Func<Task<TResult>> operation
    )
        where TException : Exception {
        try {
            return await operation.Invoke().ConfigureAwait(false);
        } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        } catch (TException ex) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResult RunOperation<TResult, TException>(
        VectorStoreMetadata metadata,
        string operationName,
        Func<TResult> operation
    )
        where TException : Exception =>
        RunOperation<TResult, TException>(
            new VectorStoreCollectionMetadata {
                CollectionName        = null,
                VectorStoreName       = metadata.VectorStoreName,
                VectorStoreSystemName = metadata.VectorStoreSystemName
            },
            operationName,
            operation);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResult RunOperation<TResult, TException>(
        VectorStoreCollectionMetadata metadata,
        string operationName,
        Func<TResult> operation
    )
        where TException : Exception {
        try {
            return operation.Invoke();
        } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        } catch (TException ex) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<TResult> RunOperationWithRetryAsync<TResult, TException>(
        VectorStoreCollectionMetadata metadata,
        string operationName,
        int maxRetries,
        int delayInMilliseconds,
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken
    )
        where TException : Exception {
        var retries = 0;

        var exceptions = new List<Exception>();

        while (retries < maxRetries) {
            try {
                return await operation.Invoke().ConfigureAwait(false);
            } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
                retries++;
                exceptions.Add(ex);

                if (retries >= maxRetries) {
                    throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                }

                await Task.Delay(delayInMilliseconds, cancellationToken).ConfigureAwait(false);
            } catch (TException ex) {
                retries++;
                exceptions.Add(ex);

                if (retries >= maxRetries) {
                    throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                }

                await Task.Delay(delayInMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
            VectorStoreSystemName = metadata.VectorStoreSystemName,
            VectorStoreName       = metadata.VectorStoreName,
            CollectionName        = metadata.CollectionName,
            OperationName         = operationName
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task RunOperationAsync<TException>(
        VectorStoreCollectionMetadata metadata,
        string operationName,
        Func<Task> operation
    )
        where TException : Exception {
        try {
            await operation.Invoke().ConfigureAwait(false);
        } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        } catch (TException ex) {
            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task RunOperationWithRetryAsync<TException>(
        VectorStoreCollectionMetadata metadata,
        string operationName,
        int maxRetries,
        int delayInMilliseconds,
        Func<Task> operation,
        CancellationToken cancellationToken
    )
        where TException : Exception {
        var retries = 0;

        var exceptions = new List<Exception>();

        while (retries < maxRetries) {
            try {
                await operation.Invoke().ConfigureAwait(false);
                return;
            } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
                retries++;
                exceptions.Add(ex);

                if (retries >= maxRetries) {
                    throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                }

                await Task.Delay(delayInMilliseconds, cancellationToken).ConfigureAwait(false);
            } catch (TException ex) {
                retries++;
                exceptions.Add(ex);

                if (retries >= maxRetries) {
                    throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                }

                await Task.Delay(delayInMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new VectorStoreException("Call to vector store failed.", new AggregateException(exceptions)) {
            VectorStoreSystemName = metadata.VectorStoreSystemName,
            VectorStoreName       = metadata.VectorStoreName,
            CollectionName        = metadata.CollectionName,
            OperationName         = operationName
        };
    }

    public static Task<bool> ReadWithErrorHandlingAsync(
        this DbDataReader reader,
        VectorStoreCollectionMetadata metadata,
        string operationName,
        CancellationToken cancellationToken
    ) =>
        RunOperationAsync<bool, DbException>(
            metadata,
            operationName,
            operation: () => reader.ReadAsync(cancellationToken));

    public static Task<bool> ReadWithErrorHandlingAsync(
        this DbDataReader reader,
        VectorStoreMetadata metadata,
        string operationName,
        CancellationToken cancellationToken
    ) =>
        RunOperationAsync<bool, DbException>(
            metadata,
            operationName,
            operation: () => reader.ReadAsync(cancellationToken));

    public static async Task<TResult> ExecuteWithErrorHandlingAsync<TResult>(
        this DbConnection connection,
        VectorStoreMetadata metadata,
        string operationName,
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken
    ) =>
        await connection
            .ExecuteWithErrorHandlingAsync(
                new VectorStoreCollectionMetadata {
                    VectorStoreSystemName = metadata.VectorStoreSystemName,
                    VectorStoreName       = metadata.VectorStoreName,
                    CollectionName        = null
                },
                operationName,
                operation,
                cancellationToken)
            .ConfigureAwait(false);

    public static async Task<TResult> ExecuteWithErrorHandlingAsync<TResult>(
        this DbConnection connection,
        VectorStoreCollectionMetadata metadata,
        string operationName,
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken
    ) {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try {
            return await operation().ConfigureAwait(false);
        } catch (DbException ex) {
            #if NET
            await connection.DisposeAsync().ConfigureAwait(false);
            #else
            connection.Dispose();
            #endif

            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        } catch (IOException ex) {
            #if NET
            await connection.DisposeAsync().ConfigureAwait(false);
            #else
            connection.Dispose();
            #endif

            throw new VectorStoreException("Call to vector store failed.", ex) {
                VectorStoreSystemName = metadata.VectorStoreSystemName,
                VectorStoreName       = metadata.VectorStoreName,
                CollectionName        = metadata.CollectionName,
                OperationName         = operationName
            };
        } catch (Exception) {
            #if NET
            await connection.DisposeAsync().ConfigureAwait(false);
            #else
            connection.Dispose();
            #endif
            throw;
        }
    }

    public struct ConfiguredCancelableErrorHandlingAsyncEnumerable<TResult, TException>
        where TException : Exception {
        readonly ConfiguredCancelableAsyncEnumerable<TResult> _enumerable;
        readonly VectorStoreCollectionMetadata                _metadata;
        readonly string                                       _operationName;

        public ConfiguredCancelableErrorHandlingAsyncEnumerable(
            ConfiguredCancelableAsyncEnumerable<TResult> enumerable,
            VectorStoreCollectionMetadata metadata,
            string operationName
        ) {
            _enumerable    = enumerable;
            _metadata      = metadata;
            _operationName = operationName;
        }

        public ConfiguredCancelableErrorHandlingAsyncEnumerable(
            ConfiguredCancelableAsyncEnumerable<TResult> enumerable,
            VectorStoreMetadata metadata,
            string operationName
        ) {
            _enumerable = enumerable;

            _metadata = new() {
                CollectionName        = null,
                VectorStoreName       = metadata.VectorStoreName,
                VectorStoreSystemName = metadata.VectorStoreSystemName
            };

            _operationName = operationName;
        }

        public Enumerator GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new(_enumerable.WithCancellation(cancellationToken).GetAsyncEnumerator(), _metadata, _operationName);

        public ConfiguredCancelableErrorHandlingAsyncEnumerable<TResult, TException> ConfigureAwait(bool continueOnCapturedContext) =>
            new(_enumerable.ConfigureAwait(continueOnCapturedContext), _metadata, _operationName);

        public struct Enumerator(
            ConfiguredCancelableAsyncEnumerable<TResult>.Enumerator enumerator,
            VectorStoreCollectionMetadata metadata,
            string operationName
        ) {
            public async ValueTask<bool> MoveNextAsync() {
                try {
                    return await enumerator.MoveNextAsync();
                } catch (AggregateException ex) when (ex.InnerException is TException innerEx) {
                    throw new VectorStoreException("Call to vector store failed.", ex) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                } catch (TException ex) {
                    throw new VectorStoreException("Call to vector store failed.", ex) {
                        VectorStoreSystemName = metadata.VectorStoreSystemName,
                        VectorStoreName       = metadata.VectorStoreName,
                        CollectionName        = metadata.CollectionName,
                        OperationName         = operationName
                    };
                }
            }

            public TResult Current => enumerator.Current;
        }
    }
}