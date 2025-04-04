using System;

namespace EventStore.Core.Services.PersistentSubscription {
	public interface IPersistentSubscriptionStreamPosition : IEquatable<IPersistentSubscriptionStreamPosition>, IComparable<IPersistentSubscriptionStreamPosition> {
		bool IsSingleStreamPosition { get; }
		long StreamEventNumber { get; }
		bool IsAllStreamPosition { get; }
		(long Commit, long Prepare) TFPosition { get; }
		bool IsLivePosition { get; }
		string ToString();
	}
}
