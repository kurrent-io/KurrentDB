// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Core.Services;

public static class SystemHeaders {
	public const string ExpectedVersion = "Kurrent-ExpectedVersion";
	public const string RequireLeader = "Kurrent-RequireLeader";
	public const string ResolveLinkTos = "Kurrent-ResolveLinkTos";
	public const string LongPoll = "Kurrent-LongPoll";
	public const string TrustedAuth = "Kurrent-TrustedAuth";
	public const string ProjectionPosition = "Kurrent-Position";
	public const string HardDelete = "Kurrent-HardDelete";
	public const string EventId = "Kurrent-EventId";
	public const string EventType = "Kurrent-EventType";
	public const string CurrentVersion = "Kurrent-CurrentVersion";

	// Legacy ES-* headers
	public const string LegacyExpectedVersion = "ES-ExpectedVersion";
	public const string LegacyRequireLeader = "ES-RequireLeader";
	public const string RequireMaster = "ES-RequireMaster"; // For backwards compatibility
	public const string LegacyResolveLinkTos = "ES-ResolveLinkTos";
	public const string LegacyLongPoll = "ES-LongPoll";
	public const string LegacyTrustedAuth = "ES-TrustedAuth";
	public const string LegacyProjectionPosition = "ES-Position";
	public const string LegacyHardDelete = "ES-HardDelete";
	public const string LegacyEventId = "ES-EventId";
	public const string LegacyEventType = "ES-EventType";
	public const string LegacyCurrentVersion = "ES-CurrentVersion";
}

public static class SystemStreams {
	public const string PersistentSubscriptionConfig = "$persistentSubscriptionConfig";
	public const string AllStream = "$all";
	public const string EventTypesStream = "$event-types";
	public const string StreamsStream = "$streams";
	public const string StreamsCreatedStream = "$streams-created";
	public const string SettingsStream = "$settings";
	public const string StatsStreamPrefix = "$stats";
	public const string ScavengesStream = "$scavenges";
	public const string EpochInformationStream = "$epoch-information";
	public const string ScavengePointsStream = "$scavengePoints";

	public const string AuthorizationPolicyRegistryStream = "$authorization-policy-settings";

	// mem streams
	public const string NodeStateStream = "$mem-node-state";
	public const string GossipStream = "$mem-gossip";

	public static bool IsSystemStream(string streamId) => streamId is ['$', ..];

	// "" is an invalid name, and so metadata cannot be set for it
	public static bool IsInvalidStream(string streamId) {
		return string.IsNullOrEmpty(streamId) || streamId == "$$";
	}

	public static string MetastreamOf(string streamId) {
		return $"$${streamId}";
	}

	public static bool IsMetastream(string streamId) => streamId is ['$', '$', ..];

	public static string OriginalStreamOf(string metastreamId) {
		return metastreamId[2..];
	}

	public static bool IsInMemoryStream(string streamId) {
		return streamId.StartsWith("$mem-");
	}
}

public static class SystemMetadata {
	public const string MaxAge = "$maxAge";
	public const string MaxCount = "$maxCount";
	public const string TruncateBefore = "$tb";
	public const string TempStream = "$tmp";
	public const string CacheControl = "$cacheControl";

	public const string Acl = "$acl";
	public const string AclRead = "$r";
	public const string AclWrite = "$w";
	public const string AclDelete = "$d";
	public const string AclMetaRead = "$mr";
	public const string AclMetaWrite = "$mw";

	public const string UserStreamAcl = "$userStreamAcl";
	public const string SystemStreamAcl = "$systemStreamAcl";
}

public static class SystemEventTypes {
	private static readonly char[] _linkToSeparator = new[] {'@'};
	public const string StreamDeleted = "$streamDeleted";
	public const string StatsCollection = "$statsCollected";
	public const string LinkTo = "$>";
	public const string StreamReference = "$@";
	public const string StreamMetadata = "$metadata";
	public const string Settings = "$settings";
	public const string StreamCreated = "$stream";
	public const string EpochInformation = "$epoch-information";

	public const string V2__StreamCreated_InIndex = "StreamCreated";
	public const string V1__StreamCreated__ = "$stream-created";
	public const string V1__StreamCreatedImplicit__ = "$stream-created-implicit";

	public const string ScavengeStarted = "$scavengeStarted";
	public const string ScavengeCompleted = "$scavengeCompleted";
	public const string ScavengeChunksCompleted = "$scavengeChunksCompleted";
	public const string ScavengeMergeCompleted = "$scavengeMergeCompleted";
	public const string ScavengeIndexCompleted = "$scavengeIndexCompleted";
	public const string EmptyEventType = "";
	public const string EventTypeDefined = "$event-type";
	public const string ScavengePoint = "$scavengePoint";

	public const string AuthorizationPolicyChanged = "$authorization-policy-changed";
	public const string PersistentSubscriptionConfig = "$PersistentConfig";

	public static string StreamReferenceEventToStreamId(string eventType, ReadOnlyMemory<byte> data) {
		string streamId;
		switch (eventType) {
			case LinkTo: {
				string[] parts = Helper.UTF8NoBom.GetString(data.Span).Split(_linkToSeparator, 2);
				streamId = parts[1];
				break;
			}
			case StreamReference:
			case V1__StreamCreated__:
			case V2__StreamCreated_InIndex: {
				streamId = Helper.UTF8NoBom.GetString(data.Span);
				break;
			}
			default:
				throw new NotSupportedException("Unknown event type: " + eventType);
		}

		return streamId;
	}

	public static string StreamReferenceEventToStreamId(string eventType, string data) {
		string streamId;
		switch (eventType) {
			case LinkTo: {
				string[] parts = data.Split(_linkToSeparator, 2);
				streamId = parts[1];
				break;
			}
			case StreamReference:
			case V1__StreamCreated__:
			case V2__StreamCreated_InIndex: {
				streamId = data;
				break;
			}
			default:
				throw new NotSupportedException("Unknown event type: " + eventType);
		}

		return streamId;
	}

	public static long EventLinkToEventNumber(string link) {
		string[] parts = link.Split(_linkToSeparator, 2);
		return long.Parse(parts[0]);
	}
}

public static class SystemRoles {
	public const string Admins = "$admins";
	public const string Operations = "$ops";
	public const string All = "$all";
}

/// <summary>
/// System supported consumer strategies for use with persistent subscriptions.
/// </summary>
public static class SystemConsumerStrategies {
	/// <summary>
	/// Distributes events to a single client until it is full. Then round robin to the next client.
	/// </summary>
	public const string DispatchToSingle = "DispatchToSingle";

	/// <summary>
	/// Distribute events to each client in a round robin fashion.
	/// </summary>
	public const string RoundRobin = "RoundRobin";

	/// <summary>
	/// Distribute events of the same streamId to the same client until it disconnects on a best efforts basis.
	/// Designed to be used with indexes such as the category projection.
	/// </summary>
	public const string Pinned = "Pinned";

	/// <summary>
	/// Distribute events of the same correlationId to the same client until it disconnects on a best efforts basis.
	/// </summary>
	public const string PinnedByCorrelation = "PinnedByCorrelation";
}
