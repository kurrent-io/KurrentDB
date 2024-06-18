using System.Runtime.InteropServices;

namespace DotNext.Threading.Leases;

using Diagnostics;

/// <summary>
/// Represents client side of a lease in a distributed environment.
/// </summary>
/// <seealso cref="LeaseProvider{TMetadata}"/>
public abstract class LeaseConsumer : Disposable
{
    private readonly double clockDriftBound;
    private LeaseIdentity identity;
    private CancellationTokenSource? source;

    /// <summary>
    /// Initializes a new lease consumer.
    /// </summary>
    protected LeaseConsumer() => clockDriftBound = 0.3D;

    /// <summary>
    /// Gets or sets wall clock desync degree in the cluster, in percents.
    /// </summary>
    /// <remarks>
    /// 0 means that wall clocks for this consumer and lease provider are in sync. To reduce contention between
    /// concurrent consumers it's better to renew a lease earlier than its expiration timeout.
    /// </remarks>
    /// <value>A value in range [0..1). The default value is 0.3.</value>
    public double ClockDriftBound
    {
        get => clockDriftBound;
        init => clockDriftBound = double.IsFinite(value) && value >= 0D ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private TimeSpan AdjustTimeToLive(TimeSpan originalTtl)
        => originalTtl - (originalTtl * clockDriftBound);

    /// <summary>
    /// Gets the token bounded to the lease lifetime.
    /// </summary>
    /// <remarks>
    /// Use that token to perform lease-bounded operation. The token acquired before a call to
    /// <see cref="TryAcquireAsync(CancellationToken)"/> or <see cref="TryRenewAsync(CancellationToken)"/>
    /// should not be used after. The typical use case to invoke these methods and then obtain the token.
    /// </remarks>
    public CancellationToken Token => source?.Token ?? new(canceled: true);

    private void CancelAndDispose()
    {
        using (source)
        {
            source?.Cancel(throwOnFirstException: false);
        }
    }

    /// <summary>
    /// Tries to acquire the lease.
    /// </summary>
    /// <remarks>
    /// This method cancels <see cref="Token"/> immediately. If the method returns <see langword="true"/>, the token
    /// can be used to perform async operation bounded to the lease lifetime.
    /// </remarks>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns><see langword="true"/> if lease taken successfully; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">The consumer is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    public async ValueTask<bool> TryAcquireAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposingOrDisposed, this);

        CancelAndDispose();

        var ts = new Timestamp();
        TimeSpan remainingTime;
        if (await TryAcquireCoreAsync(token).ConfigureAwait(false) is { } response && (remainingTime = AdjustTimeToLive(response.TimeToLive - ts.Elapsed)) > TimeSpan.Zero)
        {
            source = new();
            identity = response.Identity;
            source.CancelAfter(remainingTime);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Performs a call to <see cref="LeaseProvider{TMetadata}.TryAcquireAsync(CancellationToken)"/> across the application boundaries.
    /// </summary>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The response from the lease provider; or <see langword="null"/> if the lease cannot be taken.</returns>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    protected abstract ValueTask<AcquisitionResult?> TryAcquireCoreAsync(CancellationToken token = default);

    /// <summary>
    /// Tries to renew a lease.
    /// </summary>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns><see langword="true"/> if lease renewed successfully; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">The consumer is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="InvalidOperationException">This consumer never took the lease.</exception>
    public async ValueTask<bool> TryRenewAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposingOrDisposed, this);

        if (identity.Version is LeaseIdentity.InitialVersion)
            throw new InvalidOperationException();

        var ts = new Timestamp();
        TimeSpan remainingTime;
        if (await TryRenewCoreAsync(identity, token).ConfigureAwait(false) is { } response && (remainingTime = AdjustTimeToLive(response.TimeToLive - ts.Elapsed)) > TimeSpan.Zero)
        {
            identity = response.Identity;

            if (source is null || !TryResetOrDestroy(source))
                source = new();

            source.CancelAfter(remainingTime);
            return true;
        }

        return false;

        static bool TryResetOrDestroy(CancellationTokenSource source)
        {
            var result = source.TryReset();
            if (!result)
                source.Dispose();

            return result;
        }
    }

    /// <summary>
    /// Performs a call to <see cref="LeaseProvider{TMetadata}.TryRenewAsync(LeaseIdentity, bool, CancellationToken)"/> or
    /// <see cref="LeaseProvider{TMetadata}.TryAcquireOrRenewAsync(LeaseIdentity, CancellationToken)"/> across the application boundaries.
    /// </summary>
    /// <param name="identity">The identity of the lease to renew.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The response from the lease provider; or <see langword="null"/> if the lease cannot be taken.</returns>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    protected abstract ValueTask<AcquisitionResult?> TryRenewCoreAsync(LeaseIdentity identity, CancellationToken token);

    /// <summary>
    /// Releases a lease.
    /// </summary>
    /// <remarks>
    /// This method cancels <see cref="Token"/> immediately.
    /// </remarks>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns><see langword="true"/> if lease canceled successfully; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">The consumer is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    /// <exception cref="InvalidOperationException">This consumer never took the lease.</exception>
    /// <exception cref="InvalidOperationException">This consumer never took the lease.</exception>
    public async ValueTask<bool> ReleaseAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposingOrDisposed, this);

        if (this.identity.Version is LeaseIdentity.InitialVersion)
            throw new InvalidOperationException();

        CancelAndDispose();
        if (await ReleaseCoreAsync(this.identity, token).ConfigureAwait(false) is { } identity)
        {
            this.identity = identity;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Performs a call to <see cref="LeaseProvider{TMetadata}.ReleaseAsync(LeaseIdentity, CancellationToken)"/> across
    /// the application boundaries.
    /// </summary>
    /// <param name="identity">The identity of the lease to renew.</param>
    /// <param name="token">The token that can be used to cancel the operation.</param>
    /// <returns>The response from the lease provider; or <see langword="null"/> if the lease cannot be taken.</returns>
    /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
    protected abstract ValueTask<LeaseIdentity?> ReleaseCoreAsync(LeaseIdentity identity, CancellationToken token);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (source is not null)
            {
                source.Dispose();
                source = null;
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Represents a result of lease acquisition operation.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    protected readonly struct AcquisitionResult
    {
        /// <summary>
        /// Gets or sets the identity of the lease.
        /// </summary>
        public required LeaseIdentity Identity { get; init; }

        /// <summary>
        /// Gets or sets lease expiration time returned by the provider.
        /// </summary>
        public required TimeSpan TimeToLive { get; init; }
    }
}