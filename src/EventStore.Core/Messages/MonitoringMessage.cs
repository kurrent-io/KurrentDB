// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;
using EventStore.Core.Services.PersistentSubscription;

namespace EventStore.Core.Messages;

public static partial class MonitoringMessage {
	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetAllPersistentSubscriptionStats : Message {
		public readonly IEnvelope Envelope;
		public readonly int Offset;
		public readonly int Count;

		public GetAllPersistentSubscriptionStats(IEnvelope envelope) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			Offset = 0;
			Count = int.MaxValue;
		}

		public GetAllPersistentSubscriptionStats(IEnvelope envelope, int offset, int count) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			Offset = offset;
			Count = count;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetPersistentSubscriptionStats : Message {
		public string EventStreamId {
			get { return _eventStreamId; }
		}

		public string GroupName {
			get { return _groupName; }
		}

		public readonly IEnvelope Envelope;
		private readonly string _eventStreamId;
		private readonly string _groupName;

		public GetPersistentSubscriptionStats(IEnvelope envelope, string eventStreamId, string groupName) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			_eventStreamId = eventStreamId;
			_groupName = groupName;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetStreamPersistentSubscriptionStats : Message {
		public string EventStreamId {
			get { return _eventStreamId; }
		}

		public readonly IEnvelope Envelope;
		private readonly string _eventStreamId;

		public GetStreamPersistentSubscriptionStats(IEnvelope envelope, string eventStreamId) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			_eventStreamId = eventStreamId;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetPersistentSubscriptionStatsCompleted : Message {
		public readonly OperationStatus Result;
		public readonly List<PersistentSubscriptionInfo> SubscriptionStats;
		public string ErrorString;
		public readonly int RequestedOffset;
		public readonly int RequestedCount;
		public readonly int Total;

		public GetPersistentSubscriptionStatsCompleted(OperationStatus result,
			List<PersistentSubscriptionInfo> subscriptionStats, string errorString = "") {
			Result = result;
			SubscriptionStats = subscriptionStats;
			ErrorString = errorString;
		}

		public GetPersistentSubscriptionStatsCompleted(OperationStatus result,
			List<PersistentSubscriptionInfo> subscriptionStats, int requestedOffset, int requestedCount, int total, string errorString = "") {
			Result = result;
			SubscriptionStats = subscriptionStats;
			ErrorString = errorString;
			RequestedOffset = requestedOffset;
			RequestedCount = requestedCount;
			Total = total;
		}

		public enum OperationStatus {
			Success = 0,
			NotFound = 1,
			Fail = 2,
			NotReady = 3
		}
	}

	public class PersistentSubscriptionInfo {
		public string EventSource { get; set; }
		public string GroupName { get; set; }
		public string Status { get; set; }
		public List<ConnectionInfo> Connections { get; set; }
		public int AveragePerSecond { get; set; }
		public long TotalItems { get; set; }
		public long CountSinceLastMeasurement { get; set; }
		public string LastCheckpointedEventPosition { get; set; }
		public string LastKnownEventPosition { get; set; }
		public bool ResolveLinktos { get; set; }
		public string StartFrom { get; set; }
		public int MessageTimeoutMilliseconds { get; set; }
		public bool ExtraStatistics { get; set; }
		public int MaxRetryCount { get; set; }
		public int LiveBufferSize { get; set; }
		public int BufferSize { get; set; }
		public int ReadBatchSize { get; set; }
		public int CheckPointAfterMilliseconds { get; set; }
		public int MinCheckPointCount { get; set; }
		public int MaxCheckPointCount { get; set; }
		public int ReadBufferCount { get; set; }
		public long LiveBufferCount { get; set; }
		public int RetryBufferCount { get; set; }
		public int TotalInFlightMessages { get; set; }
		public int OutstandingMessagesCount { get; set; }
		public string NamedConsumerStrategy { get; set; }
		public int MaxSubscriberCount { get; set; }
		public long ParkedMessageCount { get; set; }
		public long OldestParkedMessage { get; set; }
	}

	public class ConnectionInfo {
		public string From { get; set; }
		public string Username { get; set; }
		public int AverageItemsPerSecond { get; set; }
		public long TotalItems { get; set; }
		public long CountSinceLastMeasurement { get; set; }
		public List<Measurement> ObservedMeasurements { get; set; }
		public int AvailableSlots { get; set; }
		public int InFlightMessages { get; set; }
		public string ConnectionName { get; set; }
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetFreshStats : Message {
		public readonly IEnvelope Envelope;
		public readonly Func<Dictionary<string, object>, Dictionary<string, object>> StatsSelector;
		public readonly bool UseMetadata;
		public readonly bool UseGrouping;

		public GetFreshStats(IEnvelope envelope,
			Func<Dictionary<string, object>, Dictionary<string, object>> statsSelector,
			bool useMetadata,
			bool useGrouping) {
			Ensure.NotNull(envelope, "envelope");
			Ensure.NotNull(statsSelector, "statsSelector");

			Envelope = envelope;
			StatsSelector = statsSelector;
			UseMetadata = useMetadata;
			UseGrouping = useGrouping;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetFreshStatsCompleted : Message {
		public readonly bool Success;
		public readonly Dictionary<string, object> Stats;

		public GetFreshStatsCompleted(bool success, Dictionary<string, object> stats) {
			Success = success;
			Stats = stats;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetFreshTcpConnectionStats : Message {
		public readonly IEnvelope Envelope;

		public GetFreshTcpConnectionStats(IEnvelope envelope) {
			Ensure.NotNull(envelope, "envelope");

			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class GetFreshTcpConnectionStatsCompleted : Message {
		public readonly List<TcpConnectionStats> ConnectionStats;

		public GetFreshTcpConnectionStatsCompleted(List<TcpConnectionStats> connectionStats) {
			ConnectionStats = connectionStats;
		}
	}

	public class TcpConnectionStats {
		public string RemoteEndPoint { get; set; }
		public string LocalEndPoint { get; set; }
		public string ClientConnectionName { get; set; }
		public Guid ConnectionId { get; set; }
		public long TotalBytesSent { get; set; }
		public long TotalBytesReceived { get; set; }
		public int PendingSendBytes { get; set; }
		public int PendingReceivedBytes { get; set; }
		public bool IsExternalConnection { get; set; }
		public bool IsSslConnection { get; set; }
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class InternalStatsRequest : Message {
		public readonly IEnvelope Envelope;

		public InternalStatsRequest(IEnvelope envelope) {
			Ensure.NotNull(envelope, "envelope");

			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Monitoring)]
	public partial class InternalStatsRequestResponse : Message {
		public readonly Dictionary<string, object> Stats;

		public InternalStatsRequestResponse(Dictionary<string, object> stats) {
			Ensure.NotNull(stats, "stats");

			Stats = stats;
		}
	}

	[DerivedMessage(CoreMessage.Misc)]
	public partial class DynamicCacheManagerTick : Message {
	}

	[DerivedMessage(CoreMessage.Misc)]
	public partial class CheckCertificateExpiry : Message {
	}

	[DerivedMessage(CoreMessage.Misc)]
	public partial class CheckEsVersion : Message {
	}
}
