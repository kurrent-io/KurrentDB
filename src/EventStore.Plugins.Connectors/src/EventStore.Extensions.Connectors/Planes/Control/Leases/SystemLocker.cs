// namespace DotNext.Threading.Leases;
//
//
// public record AcquireResult {
//     public record Acquired(ulong Token, DateTime Until) : AcquireResult;
//
//     public record Renewed(ulong Token, DateTime Until) : AcquireResult;
//
//     public record Expired : AcquireResult;
//
//     public record Taken(string By, DateTime Until) : AcquireResult;
//
//     public record InvalidToken : AcquireResult;
// }
//
// public class SystemLocker(LeaseConsumer client) {
//     LeaseConsumer Client { get; } = client;
//
//     public Task<bool> CreateLock(string resourceId, string clientId) {
//
//     }
//
// }
//
// public sealed class Lock(SystemLeaseConsumer consumer) : IDisposable {
//     public async ValueTask<AcquireResult> AcquireAsync(CancellationToken token = default) {
//
//         var success = await consumer.TryAcquireAsync(token);
//
//         if (success) {
//             return AcquireResult.Acquired(consumer.Token, consumer.);
//         }
//     }
//
//     public ValueTask<bool> RenewAsync(CancellationToken token = default) =>
//         consumer.TryRenewAsync(token);
//
//     public ValueTask<bool> ReleaseAsync(CancellationToken token = default) =>
//         consumer.ReleaseAsync(token);
//
//     public void Dispose() =>
//         consumer.Dispose();
//
//     public CancellationToken LifetimeToken =>
//         consumer.Token;
// }
//
//
//
// public class SystemLeaseConsumer(LeaseProvider<SystemLeaseMetadata> leaseProvider) : LeaseConsumer {
//     LeaseProvider<SystemLeaseMetadata> LeaseProvider { get; }       = leaseProvider;
//
//     string ResourceId { get; init; }
//     string Namespace  { get; init; }
//     string ClientId   { get; init; }
//
//     protected override async ValueTask<AcquisitionResult?> TryAcquireCoreAsync(CancellationToken token = default) {
//         var result = await LeaseProvider.TryAcquireOrRenewAsync();
//
//         return new AcquisitionResult {
//             Identity   = result!.Value.State.Identity,
//             TimeToLive = default
//         };
//     }
//
//     protected override async ValueTask<AcquisitionResult?> TryRenewCoreAsync(LeaseIdentity identity, CancellationToken token) {
//         throw new NotImplementedException();
//     }
//
//     protected override async ValueTask<LeaseIdentity?> ReleaseCoreAsync(LeaseIdentity identity, CancellationToken token) {
//         throw new NotImplementedException();
//     }
// }
//
// public record SystemLeaseMetadata(string ResourceId, string Owner);
//
// public class SystemLeaseProvider : LeaseProvider<SystemLeaseMetadata> {
//     public SystemLeaseProvider(TimeSpan ttl, TimeProvider? provider = null) : base(ttl, provider) { }
//
//     protected override async ValueTask<State> GetStateAsync(CancellationToken token) {
//         var temp = new State {
//             Metadata  = null,
//             Identity  = default,
//             CreatedAt = default
//         };
//
//         throw new NotImplementedException();
//     }
//
//     protected override async ValueTask<bool> TryUpdateStateAsync(State state, CancellationToken token) {
//         throw new NotImplementedException();
//     }
// }
//
// class kebas {
//     public kebas() {
//         var connectorLock = new SystemLeaseConsumer();
//
//         connectorLock..ReleaseAsync()
//         consumer...TryAcquireAsync().GetAwaiter().GetResult();
//     }
// }