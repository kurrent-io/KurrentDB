using System.Runtime.InteropServices;

namespace DotNext.Threading.Leases;

using Patterns;

public partial class LeaseProvider<TMetadata>
{
    private interface ITransitionCondition
    {
        bool Invoke(in State state, TimeProvider provider, TimeSpan timeToLive, out TimeSpan remainingTime);
    }

    private sealed class AcqusitionCondition : ITransitionCondition, ISingleton<AcqusitionCondition>
    {
        public static AcqusitionCondition Instance { get; } = new();

        private AcqusitionCondition()
        {
        }

        bool ITransitionCondition.Invoke(in State state, TimeProvider provider, TimeSpan timeToLive, out TimeSpan remainingTime)
            => state.IsExpired(provider, timeToLive, out remainingTime);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct RenewalCondition(LeaseIdentity identity, bool reacquire) : ITransitionCondition
    {
        bool ITransitionCondition.Invoke(in State state, TimeProvider provider, TimeSpan timeToLive, out TimeSpan remainingTime)
        {
            remainingTime = timeToLive;
            return state.Identity == identity && (reacquire || !state.IsExpired(provider, timeToLive, out remainingTime));
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct AcquisitionOrRenewalCondition(LeaseIdentity identity) : ITransitionCondition
    {
        bool ITransitionCondition.Invoke(in State state, TimeProvider provider, TimeSpan timeToLive, out TimeSpan remainingTime)
        {
            remainingTime = timeToLive;
            return state.Identity == identity || state.IsExpired(provider, timeToLive, out remainingTime);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Updater<TArg>(TArg arg, Func<TArg, TMetadata, CancellationToken, ValueTask<TMetadata>> updater) : ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>
    {
        ValueTask<TMetadata> ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>.Invoke(TMetadata metadata, CancellationToken token)
            => updater(arg, metadata, token);
    }

    private sealed class NoOpUpdater : ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>, ISingleton<NoOpUpdater>
    {
        public static NoOpUpdater Instance { get; } = new();

        private NoOpUpdater()
        {
        }

        ValueTask<TMetadata> ISupplier<TMetadata, CancellationToken, ValueTask<TMetadata>>.Invoke(TMetadata metadata, CancellationToken token)
            => ValueTask.FromResult(metadata);
    }
}