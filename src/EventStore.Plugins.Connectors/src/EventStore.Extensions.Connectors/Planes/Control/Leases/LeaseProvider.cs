using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DotNext.Threading.Leases;

using Diagnostics;

/// <summary>
/// Represents provider side of a lease in a distributed environment.
/// </summary>
/// <remarks>
/// An instance of this type must support concurrent calls.
/// </remarks>
/// <typeparam name="TMetadata">The type of metadata associated with a lease.</typeparam>
/// <seealso cref="LeaseConsumer"/>
public abstract partial class LeaseProvider<TMetadata> : Disposable
{
    private readonly TimeProvider provider;

    [SuppressMessage("Usage", "CA2213", Justification = "Disposed using DestroyLease() method")]
    private volatile CancellationTokenSource? lifetimeTokenSource;

    /// <summary>
    /// Initializes a new instance of lease provider.
    /// </summary>
    /// <param name="ttl">The lease expiration timeout.</param>
    /// <param name="provider">The time provider.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is not positive.</exception>
    protected LeaseProvider(TimeSpan ttl, TimeProvider? provider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        this.provider = provider ?? TimeProvider.System;
        TimeToLive = ttl;
        lifetimeTokenSource = new();
        LifetimeToken = lifetimeTokenSource.Token;
    }

    /// <summary>
    /// A token that represents state of this object.
    /// </summary>
    /// <remarks>
    /// A call to <see cref="Dispose(bool)"/> cancels the token.
    /// </remarks>
    protected CancellationToken LifetimeToken { get; } // cached to avoid ObjectDisposedException

    /// <summary>
    /// Gets a lease time-to-live.
    /// </summary>
    public TimeSpan TimeToLive { get; }

    private async ValueTask<AcquisitionResult?> TryChangeStateAsync<TCondition, TUpdater>(TCondition condition, TUpdater updater, CancellationToken token)
        where TCondition : notnull, ITransitionCondition
        where TUpdater : notnull, ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        var cts = token.LinkTo(LifetimeToken);
        try
        {
            var state = await GetStateAsync(token).ConfigureAwait(false);

            if (!condition.Invoke(in state, provider, TimeToLive, out var remainingTime))
                return null;

            state = state with
            {
                CreatedAt = provider.GetUtcNow(),
                Identity = state.Identity.BumpVersion(),
                Metadata = await updater.Invoke(state.Metadata, token).ConfigureAwait(false),
            };

            var ts = new Timestamp();
            if (!await TryUpdateStateAsync(state, token).ConfigureAwait(false))
                return null;

            remainingTime = TimeToLive - ts.Elapsed;
            return remainingTime > TimeSpan.Zero
                ? new(in state, remainingTime)
                : null;
        }
        catch (OperationCanceledException e) //when (e.CausedBy(cts, LifetimeToken))
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Tries to acquire the lease.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The acquistion result; or <see langword="null"/> if the lease is already taken.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<AcquisitionResult?> TryAcquireAsync<TArg>(TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? TryChangeStateAsync(AcqusitionCondition.Instance, new Updater<TArg>(arg, updater), token) : ValueTask.FromException<AcquisitionResult?>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Tries to acquire the lease.
    /// </summary>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The acquistion result; or <see langword="null"/> if the lease is already taken.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<AcquisitionResult?> TryAcquireAsync(CancellationToken token = default)
        => TryChangeStateAsync(AcqusitionCondition.Instance, NoOpUpdater.Instance, token);

    private async ValueTask<AcquisitionResult> AcquireAsync<TUpdater>(TUpdater updater, CancellationToken token)
        where TUpdater : notnull, ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        var cts = token.LinkTo(LifetimeToken);
        try
        {
            while (true)
            {
                var state = await GetStateAsync(token).ConfigureAwait(false);

                if (state.IsExpired(provider, TimeToLive, out var remainingTime))
                {
                    state = state with
                    {
                        CreatedAt = provider.GetUtcNow(),
                        Identity = state.Identity.BumpVersion(),
                        Metadata = await updater.Invoke(state.Metadata, token).ConfigureAwait(false),
                    };

                    var ts = new Timestamp();
                    if (!await TryUpdateStateAsync(state, token).ConfigureAwait(false))
                        continue;

                    remainingTime = TimeToLive - ts.Elapsed;

                    if (remainingTime <= TimeSpan.Zero)
                        continue;

                    return new(in state, remainingTime);
                }

                await Task.Delay(remainingTime, provider, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException e) //when (e.CausedBy(cts, LifetimeToken))
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Acquires the lease.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<AcquisitionResult> AcquireAsync<TArg>(TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? AcquireAsync(new Updater<TArg>(arg, updater), token) : ValueTask.FromException<AcquisitionResult>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Acquires the lease.
    /// </summary>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<AcquisitionResult> AcquireAsync(CancellationToken token = default)
        => AcquireAsync(NoOpUpdater.Instance, token);

    /// <summary>
    /// Tries to renew the lease.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="reacquire"><see langword="true"/> to acquire the lease on renewal if it is expired.</param>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation; or <see langword="null"/> if the lease is taken by another process or expired.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<AcquisitionResult?> TryRenewAsync<TArg>(LeaseIdentity identity, bool reacquire, TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? TryChangeStateAsync(new RenewalCondition(identity, reacquire), new Updater<TArg>(arg, updater), token) : ValueTask.FromException<AcquisitionResult?>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Tries to renew the lease.
    /// </summary>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="reacquire"><see langword="true"/> to acquire the lease on renewal if it is expired.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation; or <see langword="null"/> if the lease is taken by another process or expired.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<AcquisitionResult?> TryRenewAsync(LeaseIdentity identity, bool reacquire, CancellationToken token = default)
        => TryChangeStateAsync(new RenewalCondition(identity, reacquire), NoOpUpdater.Instance, token);

    private async ValueTask<LeaseIdentity?> ReleaseAsync<TUpdater>(LeaseIdentity identity, TUpdater updater, CancellationToken token)
        where TUpdater : notnull, ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        if (identity.Version is LeaseIdentity.InitialVersion)
            return null;

        var cts = token.LinkTo(LifetimeToken);
        try
        {
            var state = await GetStateAsync(token).ConfigureAwait(false);

            if (state.IsExpired(provider, TimeToLive, out _) || state.Identity != identity)
                return null;

            state = state with
            {
                CreatedAt = default,
                Identity = identity.BumpVersion(),
                Metadata = await updater.Invoke(state.Metadata, token).ConfigureAwait(false),
            };

            return await TryUpdateStateAsync(state, token).ConfigureAwait(false) ? state.Identity : null;
        }
        catch (OperationCanceledException e) //when (e.CausedBy(cts, LifetimeToken))
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Releases the lease.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>Updated lease identity; or <see langword="null"/> if expired or taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<LeaseIdentity?> ReleaseAsync<TArg>(LeaseIdentity identity, TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? ReleaseAsync(identity, new Updater<TArg>(arg, updater), token) : ValueTask.FromException<LeaseIdentity?>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Releases the lease.
    /// </summary>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>Updated lease identity; or <see langword="null"/> if expired or taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<LeaseIdentity?> ReleaseAsync(LeaseIdentity identity, CancellationToken token = default)
        => ReleaseAsync(identity, NoOpUpdater.Instance, token);

    private async ValueTask<LeaseIdentity?> UnsafeTryReleaseAsync<TUpdater>(TUpdater updater, CancellationToken token)
        where TUpdater : notnull, ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        var cts = token.LinkTo(LifetimeToken);
        try
        {
            var state = await GetStateAsync(token).ConfigureAwait(false);

            state = state with
            {
                CreatedAt = default,
                Identity = state.Identity.BumpVersion(),
                Metadata = await updater.Invoke(state.Metadata, token).ConfigureAwait(false),
            };

            return await TryUpdateStateAsync(state, token).ConfigureAwait(false) ? state.Identity : null;
        }
        catch (OperationCanceledException e) //when (e.CausedBy(cts, LifetimeToken))
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Tries to release the lease ungracefully.
    /// </summary>
    /// <remarks>
    /// It's possible to call <see cref="AcquireAsync(CancellationToken)"/> method after the current one
    /// and get a new lease while the existing owner thinks that the lease is exclusively owned.
    /// </remarks>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>Updated lease identity; or <see langword="null"/> if updated or taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<LeaseIdentity?> UnsafeTryReleaseAsync<TArg>(TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? UnsafeTryReleaseAsync(new Updater<TArg>(arg, updater), token) : ValueTask.FromException<LeaseIdentity?>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Tries to release the lease ungracefully.
    /// </summary>
    /// <remarks>
    /// It's possible to call <see cref="AcquireAsync(CancellationToken)"/> method after the current one
    /// and get a new lease while the existing owner thinks that the lease is exclusively owned.
    /// </remarks>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>Updated lease identity; or <see langword="null"/> if updated or taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<LeaseIdentity?> UnsafeTryReleaseAsync(CancellationToken token = default)
        => UnsafeTryReleaseAsync(NoOpUpdater.Instance, token);

    private async ValueTask<LeaseIdentity> UnsafeReleaseAsync<TUpdater>(TUpdater updater, CancellationToken token)
        where TUpdater : notnull, ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        var cts = token.LinkTo(LifetimeToken);
        try
        {
            State state;
            do
            {
                state = await GetStateAsync(token).ConfigureAwait(false);

                state = state with
                {
                    CreatedAt = default,
                    Identity = state.Identity.BumpVersion(),
                    Metadata = await updater.Invoke(state.Metadata, token).ConfigureAwait(false),
                };
            }
            while (!await TryUpdateStateAsync(state, token).ConfigureAwait(false));

            return state.Identity;
        }
        catch (OperationCanceledException e) //when (e.CausedBy(cts, LifetimeToken))
        {
            throw new ObjectDisposedException(GetType().Name);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Releases the lease ungracefully.
    /// </summary>
    /// <remarks>
    /// It's possible to call <see cref="AcquireAsync(CancellationToken)"/> method after the current one
    /// and get a new lease while the existing owner thinks that the lease is exclusively owned.
    /// </remarks>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="updater"/> is <see langword="null"/>.</exception>
    public ValueTask<LeaseIdentity> UnsafeReleaseAsync<TArg>(TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? UnsafeReleaseAsync(new Updater<TArg>(arg, updater), token) : ValueTask.FromException<LeaseIdentity>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Releases the lease ungracefully.
    /// </summary>
    /// <remarks>
    /// It's possible to call <see cref="AcquireAsync(CancellationToken)"/> method after the current one
    /// and get a new lease while the existing owner thinks that the lease is exclusively owned.
    /// </remarks>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<LeaseIdentity> UnsafeReleaseAsync(CancellationToken token = default)
        => UnsafeReleaseAsync(NoOpUpdater.Instance, token);

    /// <summary>
    /// Tries to acquire or renew the lease.
    /// </summary>
    /// <typeparam name="TArg">The type of the argument to be passed to the metadata updater.</typeparam>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="arg">The argument to be passed to the metadata updater.</param>
    /// <param name="updater">An idempotent operation to update the metadata on successful acquisition of the lease.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation; or <see langword="null"/> if the lease is taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<AcquisitionResult?> TryAcquireOrRenewAsync<TArg>(LeaseIdentity identity, TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater, CancellationToken token = default)
        => updater is not null ? TryChangeStateAsync(new AcquisitionOrRenewalCondition(identity), new Updater<TArg>(arg, updater), token) : ValueTask.FromException<AcquisitionResult?>(new ArgumentNullException(nameof(updater)));

    /// <summary>
    /// Tries to acquire or renew the lease.
    /// </summary>
    /// <param name="identity">The identity of the lease obtained from <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="AcquireAsync(CancellationToken)"/>.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The status of the operation; or <see langword="null"/> if the lease is taken by another process.</returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public ValueTask<AcquisitionResult?> TryAcquireOrRenewAsync(LeaseIdentity identity, CancellationToken token = default)
        => TryChangeStateAsync(new AcquisitionOrRenewalCondition(identity), NoOpUpdater.Instance, token);

    /// <summary>
    /// Loads the state of a lease from the underlying storage.
    /// </summary>
    /// <remarks>
    /// The method can be called concurrently.
    /// </remarks>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The state restored from the underlying storage.</returns>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    protected abstract ValueTask<State> GetStateAsync(CancellationToken token);

    /// <summary>
    /// Attempts to update the state in the underlying storage using compare-and-set semantics.
    /// </summary>
    /// <remarks>
    /// The operation must use compare-and-set semantics in the following way: save <paramref name="state"/>
    /// only if <see cref="LeaseIdentity.Version"/> of the currently stored state is equal to the version of <paramref name="state"/> - 1.
    /// Note that the method can be called concurrently.
    /// </remarks>
    /// <param name="state">The state to be stored in the underlying storage.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns><see langword="true"/> if update is performed successfully; <see langword="false"/> is compare-and-set failed.</returns>
    protected abstract ValueTask<bool> TryUpdateStateAsync(State state, CancellationToken token);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Interlocked.Exchange(ref lifetimeTokenSource, null) is { } cts)
            {
                try
                {
                    cts.Cancel(throwOnFirstException: false);
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Represents a state of the lease.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct State
    {
        /// <summary>
        /// The metadata associated with the lease.
        /// </summary>
        public TMetadata Metadata { get; init; }

        /// <summary>
        /// A version of the lease.
        /// </summary>
        /// <remarks>
        /// Must be set of <see cref="LeaseIdentity.InitialVersion"/> if there is no state in the underlying persistent storage.
        /// </remarks>
        public required LeaseIdentity Identity { get; init; }

        /// <summary>
        /// A timestamp of when this state was created.
        /// </summary>
        public required DateTimeOffset CreatedAt { get; init; }

        internal bool IsExpired(TimeProvider provider, TimeSpan ttl, out TimeSpan remaining)
            => (remaining = provider.GetUtcNow() - CreatedAt) < ttl;
    }

    /// <summary>
    /// Represents a result of lease acquisition operation.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct AcquisitionResult
    {
        /// <summary>
        /// A version of the lease.
        /// </summary>
        public readonly State State;

        /// <summary>
        /// The remaining lease time.
        /// </summary>
        public readonly Timeout Lifetime;

        internal AcquisitionResult(in State state, TimeSpan ttl)
        {
            Debug.Assert(ttl > TimeSpan.Zero);

            State = state;
            Lifetime = new(ttl);
        }

        /// <summary>
        /// Deconstructs the result.
        /// </summary>
        /// <param name="state">Same as <see cref="AcquisitionResult.State"/>.</param>
        /// <param name="lifetime">Same as <paramref name="lifetime"/>.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out State state, out Timeout lifetime)
        {
            state = State;
            lifetime = Lifetime;
        }
    }
}