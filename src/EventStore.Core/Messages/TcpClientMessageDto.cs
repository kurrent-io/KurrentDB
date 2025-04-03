// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using Google.Protobuf;

// ReSharper disable once CheckNamespace
namespace EventStore.Client.Messages;

partial class NewEvent {
	public NewEvent(byte[] eventId, string eventType, int dataContentType, int metadataContentType, byte[] data, byte[] metadata)
	{
	
		EventId = ByteString.CopyFrom(eventId);
		EventType = eventType;
		DataContentType = dataContentType;
		MetadataContentType = metadataContentType;
		Data = ByteString.CopyFrom(data);
		Metadata = ByteString.CopyFrom(metadata);
	}
}

partial class EventRecord {
	public EventRecord(string eventStreamId, long eventNumber, byte[] eventId, string eventType, int dataContentType, int metadataContentType, byte[] data, byte[] metadata, long created, long createdEpoch) {
		AssignValues(eventStreamId, eventNumber, eventId, eventType, dataContentType, metadataContentType, data, metadata, created, createdEpoch);
	}

	private void AssignValues(string eventStreamId, long eventNumber, byte[] eventId, string eventType, int dataContentType,
		int metadataContentType, byte[] data, byte[] metadata, long created, long createdEpoch)
	{
		EventStreamId = eventStreamId;
		EventNumber = eventNumber;
		EventId = ByteString.CopyFrom(eventId);
		EventType = eventType;
		DataContentType = dataContentType;
		MetadataContentType = metadataContentType;
		Data = ByteString.CopyFrom(data);
		Metadata = ByteString.CopyFrom(metadata);
		Created = created;
		CreatedEpoch = createdEpoch;
	}

	public EventRecord(Core.Data.EventRecord eventRecord) {
		if (eventRecord == null) return;
		AssignValues(
			eventRecord.EventStreamId,
			eventRecord.EventNumber,
			eventRecord.EventId.ToByteArray(),
			eventRecord.EventType,
			eventRecord.IsJson ? 1 : 0,
			eventRecord.IsJson ? 1 : 0,
			eventRecord.Data.ToArray(),
			eventRecord.Metadata.ToArray(),
			eventRecord.TimeStamp.ToBinary(),
			(long)(eventRecord.TimeStamp - new DateTime(1970, 1, 1)).TotalMilliseconds
			);
	}

	public EventRecord(Core.Data.EventRecord eventRecord, long eventNumber) {
		if (eventRecord == null) return;
		AssignValues(
			eventRecord.EventStreamId,
			eventNumber,
			eventRecord.EventId.ToByteArray(),
			eventRecord.EventType,
			eventRecord.IsJson ? 1 : 0,
			eventRecord.IsJson ? 1 : 0,
			eventRecord.Data.ToArray(),
			eventRecord.Metadata.ToArray(),
			eventRecord.TimeStamp.ToBinary(),
			(long)(eventRecord.TimeStamp - new DateTime(1970, 1, 1)).TotalMilliseconds
		);
	}
}

partial class ResolvedIndexedEvent {
	public ResolvedIndexedEvent(EventRecord @event, EventRecord link)
	{
		Event = @event;
		Link = link;
	}

	public ResolvedIndexedEvent(Core.Data.EventRecord @event, Core.Data.EventRecord link) {
		if (@event != null) Event = new EventRecord(@event);
		if (link != null) Link = new EventRecord(link);
	}
}

partial class ResolvedEvent {
	public ResolvedEvent(EventRecord @event, EventRecord link, long commitPosition, long preparePosition)
	{
		if (@event != null) Event = @event;
		if (link != null) Link = link;
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
	}

	public ResolvedEvent(Core.Data.ResolvedEvent pair)
		: this(pair.Event != null ? new EventRecord(pair.Event) : null,
			pair.Link != null ? new EventRecord(pair.Link) : null,
			pair.OriginalPosition?.CommitPosition ?? -1,
			pair.OriginalPosition?.PreparePosition ?? -1) {
	}
}

partial class WriteEvents {
	public WriteEvents(string eventStreamId, long expectedVersion, NewEvent[] events, bool requireLeader)
	{
		EventStreamId = eventStreamId;
		ExpectedVersion = expectedVersion;
		Events.AddRange(events);
		RequireLeader = requireLeader;
	}
}

partial class WriteEventsCompleted {
	public WriteEventsCompleted(OperationResult result, string message, long firstEventNumber, long lastEventNumber, long preparePosition, long commitPosition, long currentVersion)
	{
		Result = result;
		if(message != null) Message = message;
		FirstEventNumber = firstEventNumber;
		LastEventNumber = lastEventNumber;
		PreparePosition = preparePosition;
		CommitPosition = commitPosition;
		CurrentVersion = currentVersion;
	}
}

partial class DeleteStream {
	public DeleteStream(string eventStreamId, long expectedVersion, bool requireLeader, bool hardDelete)
	{
		EventStreamId = eventStreamId;
		ExpectedVersion = expectedVersion;
		RequireLeader = requireLeader;
		HardDelete = hardDelete;
	}
}

partial class DeleteStreamCompleted {
	public DeleteStreamCompleted(OperationResult result, string message, long currentVersion, long preparePosition,
		long commitPosition) {
		Result = result;
		if(!string.IsNullOrEmpty(message)) Message = message;
		CurrentVersion = currentVersion;
		PreparePosition = preparePosition;
		CommitPosition = commitPosition;
	}
}

partial class TransactionStart {
	public TransactionStart(string eventStreamId, long expectedVersion, bool requireLeader)
	{
		EventStreamId = eventStreamId;
		ExpectedVersion = expectedVersion;
		RequireLeader = requireLeader;
	}
}

partial class TransactionStartCompleted {
	public TransactionStartCompleted(long transactionId, OperationResult result, string message)
	{
		TransactionId = transactionId;
		Result = result;
		if(message != null) Message = message;
	}
}

partial class TransactionWrite {
	public TransactionWrite(long transactionId, NewEvent[] events, bool requireLeader)
	{
		TransactionId = transactionId;
		Events.AddRange(events);
		RequireLeader = requireLeader;
	}
}

partial class TransactionWriteCompleted {
	public TransactionWriteCompleted(long transactionId, OperationResult result, string message)
	{
		TransactionId = transactionId;
		Result = result;
		if(message != null) Message = message;
	}
}

partial class TransactionCommit {
	public TransactionCommit(long transactionId, bool requireLeader)
	{
		TransactionId = transactionId;
		RequireLeader = requireLeader;
	}
}

partial class TransactionCommitCompleted {
	public TransactionCommitCompleted(long transactionId, OperationResult result, string message, long firstEventNumber, long lastEventNumber, long preparePosition, long commitPosition)
	{
		TransactionId = transactionId;
		Result = result;
		if(message != null) Message = message;
		FirstEventNumber = firstEventNumber;
		LastEventNumber = lastEventNumber;
		PreparePosition = preparePosition;
		CommitPosition = commitPosition;
	}
}

partial class ReadEvent {
	public ReadEvent(string eventStreamId, long eventNumber, bool resolveLinkTos, bool requireLeader)
	{
		EventStreamId = eventStreamId;
		EventNumber = eventNumber;
		ResolveLinkTos = resolveLinkTos;
		RequireLeader = requireLeader;
	}
}

partial class ReadEventCompleted {
	public ReadEventCompleted(ReadEventCompleted.Types.ReadEventResult result, ResolvedIndexedEvent @event, string error)
	{
		Result = result;
		if (@event != null) Event = @event;
		if (error != null) Error = error;
	}
}

partial class ReadStreamEvents {
	public ReadStreamEvents(string eventStreamId, long fromEventNumber, int maxCount, bool resolveLinkTos, bool requireLeader)
	{
		EventStreamId = eventStreamId;
		FromEventNumber = fromEventNumber;
		MaxCount = maxCount;
		ResolveLinkTos = resolveLinkTos;
		RequireLeader = requireLeader;
	}
}

partial class ReadStreamEventsCompleted {
	public ReadStreamEventsCompleted(ResolvedIndexedEvent[] events, ReadStreamEventsCompleted.Types.ReadStreamResult result, long nextEventNumber, long lastEventNumber, bool isEndOfStream, long lastCommitPosition, string error)
	{
		Events.AddRange(events);
		Result = result;
		NextEventNumber = nextEventNumber;
		LastEventNumber = lastEventNumber;
		IsEndOfStream = isEndOfStream;
		LastCommitPosition = lastCommitPosition;
		if(error != null) Error = error;
	}
}

partial class ReadAllEvents {
	public ReadAllEvents(long commitPosition, long preparePosition, int maxCount, bool resolveLinkTos, bool requireLeader)
	{
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
		MaxCount = maxCount;
		ResolveLinkTos = resolveLinkTos;
		RequireLeader = requireLeader;
	}
}

partial class ReadAllEventsCompleted {
	public ReadAllEventsCompleted(long commitPosition, long preparePosition, ResolvedEvent[] events, long nextCommitPosition, long nextPreparePosition, ReadAllEventsCompleted.Types.ReadAllResult result, string error)
	{
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
		Events.AddRange(events);
		NextCommitPosition = nextCommitPosition;
		NextPreparePosition = nextPreparePosition;
		Result = result;
		if(error != null) Error = error;
	}
}

partial class Filter {
	public Filter(Filter.Types.FilterContext context, Filter.Types.FilterType type, string[] data)
	{
		Context = context;
		Type = type;
		Data.AddRange(data);
	}
}

partial class FilteredReadAllEvents {
	public FilteredReadAllEvents(long commitPosition, long preparePosition, int maxCount, int maxSearchWindow, bool resolveLinkTos, bool requireLeader, Filter filter)
	{
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
		MaxCount = maxCount;
		MaxSearchWindow = maxSearchWindow;
		ResolveLinkTos = resolveLinkTos;
		RequireLeader = requireLeader;
		Filter = filter;
	}
}

partial class FilteredReadAllEventsCompleted {
	public FilteredReadAllEventsCompleted(long commitPosition, long preparePosition, ResolvedEvent[] events, long nextCommitPosition, long nextPreparePosition, bool isEndOfStream, FilteredReadAllEventsCompleted.Types.FilteredReadAllResult result, string error)
	{
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
		Events.AddRange(events);
		NextCommitPosition = nextCommitPosition;
		NextPreparePosition = nextPreparePosition;
		IsEndOfStream = isEndOfStream;
		Result = result;
		if(error != null) Error = error;
	}
}

partial class CreatePersistentSubscription {
	public CreatePersistentSubscription(string subscriptionGroupName, string eventStreamId, bool resolveLinkTos, long startFrom, int messageTimeoutMilliseconds, bool recordStatistics, int liveBufferSize, int readBatchSize, int bufferSize, int maxRetryCount, bool preferRoundRobin, int checkpointAfterTime, int checkpointMaxCount, int checkpointMinCount, int subscriberMaxCount, string namedConsumerStrategy)
	{
		SubscriptionGroupName = subscriptionGroupName;
		EventStreamId = eventStreamId;
		ResolveLinkTos = resolveLinkTos;
		StartFrom = startFrom;
		MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
		RecordStatistics = recordStatistics;
		LiveBufferSize = liveBufferSize;
		ReadBatchSize = readBatchSize;
		BufferSize = bufferSize;
		MaxRetryCount = maxRetryCount;
		PreferRoundRobin = preferRoundRobin;
		CheckpointAfterTime = checkpointAfterTime;
		CheckpointMaxCount = checkpointMaxCount;
		CheckpointMinCount = checkpointMinCount;
		SubscriberMaxCount = subscriberMaxCount;
		NamedConsumerStrategy = namedConsumerStrategy;
	}
}

partial class CreatePersistentSubscriptionCompleted {
	public CreatePersistentSubscriptionCompleted(CreatePersistentSubscriptionCompleted.Types.CreatePersistentSubscriptionResult result, string reason) {
		Result = result;
		if (reason != null) Reason = reason;
	}
}

partial class DeletePersistentSubscription {
	public DeletePersistentSubscription(string subscriptionGroupName, string eventStreamId)
	{
		SubscriptionGroupName = subscriptionGroupName;
		EventStreamId = eventStreamId;
	}
}

partial class DeletePersistentSubscriptionCompleted {
	public DeletePersistentSubscriptionCompleted(Types.DeletePersistentSubscriptionResult result, string reason) {
		Result = result;
		Reason = reason;
	}
}

partial class UpdatePersistentSubscription {
	public UpdatePersistentSubscription(string subscriptionGroupName, string eventStreamId, bool resolveLinkTos, long startFrom, int messageTimeoutMilliseconds, bool recordStatistics, int liveBufferSize, int readBatchSize, int bufferSize, int maxRetryCount, bool preferRoundRobin, int checkpointAfterTime, int checkpointMaxCount, int checkpointMinCount, int subscriberMaxCount, string namedConsumerStrategy)
	{
		SubscriptionGroupName = subscriptionGroupName;
		EventStreamId = eventStreamId;
		ResolveLinkTos = resolveLinkTos;
		StartFrom = startFrom;
		MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
		RecordStatistics = recordStatistics;
		LiveBufferSize = liveBufferSize;
		ReadBatchSize = readBatchSize;
		BufferSize = bufferSize;
		MaxRetryCount = maxRetryCount;
		PreferRoundRobin = preferRoundRobin;
		CheckpointAfterTime = checkpointAfterTime;
		CheckpointMaxCount = checkpointMaxCount;
		CheckpointMinCount = checkpointMinCount;
		SubscriberMaxCount = subscriberMaxCount;
		NamedConsumerStrategy = namedConsumerStrategy;
	}
}

partial class UpdatePersistentSubscriptionCompleted {
	public UpdatePersistentSubscriptionCompleted(UpdatePersistentSubscriptionCompleted.Types.UpdatePersistentSubscriptionResult result, string reason) {
		Result = result;
		if (reason != null) Reason = reason;
	}
}

partial class ConnectToPersistentSubscription {
	public ConnectToPersistentSubscription(string subscriptionId, string eventStreamId, int allowedInFlightMessages)
	{
		SubscriptionId = subscriptionId;
		EventStreamId = eventStreamId;
		AllowedInFlightMessages = allowedInFlightMessages;
	}
}

partial class PersistentSubscriptionConfirmation {
	public PersistentSubscriptionConfirmation(long lastCommitPosition, string subscriptionId, long lastEventNumber)
	{
		LastCommitPosition = lastCommitPosition;
		SubscriptionId = subscriptionId;
		LastEventNumber = lastEventNumber;
	}
}

partial class PersistentSubscriptionAckEvents {
	public PersistentSubscriptionAckEvents(string subscriptionId, byte[][] processedEventIds)
	{
		SubscriptionId = subscriptionId;
		for (int i = 0; i < processedEventIds.Length; i++) {
			ProcessedEventIds.Add(ByteString.CopyFrom(processedEventIds[i]));
		}
	}
}

partial class PersistentSubscriptionNakEvents {
	public PersistentSubscriptionNakEvents(string subscriptionId, byte[][] processedEventIds, string message, PersistentSubscriptionNakEvents.Types.NakAction action)
	{
		SubscriptionId = subscriptionId;
		for (int i = 0; i < processedEventIds.Length; i++) {
			ProcessedEventIds.Add(ByteString.CopyFrom(processedEventIds[i]));
		}

		if (message != null) Message = message;
		Action = action;
	}
}

partial class PersistentSubscriptionStreamEventAppeared {
	public PersistentSubscriptionStreamEventAppeared(ResolvedIndexedEvent @event, int retryCount)
	{
		Event = @event;
		RetryCount = retryCount;
	}
}

partial class SubscribeToStream {
	public SubscribeToStream(string eventStreamId, bool resolveLinkTos)
	{
		EventStreamId = eventStreamId;
		ResolveLinkTos = resolveLinkTos;
	}
}

partial class FilteredSubscribeToStream {
	public FilteredSubscribeToStream(string eventStreamId, bool resolveLinkTos, Filter filter, int checkpointInterval)
	{
		EventStreamId = eventStreamId;
		ResolveLinkTos = resolveLinkTos;
		Filter = filter;
		CheckpointInterval = checkpointInterval;
	}
}

partial class CheckpointReached {
	public CheckpointReached(long commitPosition, long preparePosition)
	{
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
	}
}

partial class SubscriptionConfirmation {
	public SubscriptionConfirmation(long lastCommitPosition, long lastEventNumber)
	{
		LastCommitPosition = lastCommitPosition;
		LastEventNumber = lastEventNumber;
	}
}

partial class StreamEventAppeared {
	public StreamEventAppeared(ResolvedEvent @event) {
		Event = @event;
	}
}

partial class SubscriptionDropped {
	public SubscriptionDropped(SubscriptionDropped.Types.SubscriptionDropReason reason)
	{
		Reason = reason;
	}
}

partial class NotHandled {
	public NotHandled(ClientMessage.NotHandled source) {
		Reason = source.Reason switch {
			ClientMessage.NotHandled.Types.NotHandledReason.NotReady => Types.NotHandledReason.NotReady,
			ClientMessage.NotHandled.Types.NotHandledReason.TooBusy => Types.NotHandledReason.TooBusy,
			ClientMessage.NotHandled.Types.NotHandledReason.NotLeader => Types.NotHandledReason.NotLeader,
			ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => Types.NotHandledReason.IsReadOnly,
			_ => throw new ArgumentOutOfRangeException()
		};
		//this is horrible and only for transport compatibility purposes
		if(source.LeaderInfo != null) AdditionalInfo = new Types.LeaderInfo(source.LeaderInfo.ExternalTcp, source.LeaderInfo.IsSecure, source.LeaderInfo.Http).ToByteString();
	}
	partial class Types {
		partial class LeaderInfo {
			public LeaderInfo(string externalTcpAddress, int externalTcpPort, string httpAddress, int httpPort, string externalSecureTcpAddress, int externalSecureTcpPort)
			{
				ExternalTcpAddress = externalTcpAddress;
				ExternalTcpPort = externalTcpPort;
				HttpAddress = httpAddress;
				HttpPort = httpPort;
				ExternalSecureTcpAddress = externalSecureTcpAddress;
				ExternalSecureTcpPort = externalSecureTcpPort;
			}

			public LeaderInfo(EndPoint tcpEndPoint, bool isTcpEndPointSecure, EndPoint httpEndPoint) {
				if (isTcpEndPointSecure) {
					ExternalSecureTcpAddress = tcpEndPoint?.GetHost();
					ExternalSecureTcpPort = tcpEndPoint?.GetPort() ?? 0;
				} else {
					ExternalTcpAddress = tcpEndPoint?.GetHost();
					ExternalTcpPort = tcpEndPoint?.GetPort() ?? 0;
				}

				HttpAddress = httpEndPoint.GetHost();
				HttpPort = httpEndPoint.GetPort();
			}
		}
	}
}

partial class ScavengeDatabaseResponse {
	public ScavengeDatabaseResponse(ScavengeDatabaseResponse.Types.ScavengeResult result, string scavengeId)
	{
		Result = result;
		ScavengeId = scavengeId;
	}
}

partial class IdentifyClient {
	public IdentifyClient(int version, string connectionName) {
		Version = version;
		if (connectionName != null) ConnectionName = connectionName;
	}
}
