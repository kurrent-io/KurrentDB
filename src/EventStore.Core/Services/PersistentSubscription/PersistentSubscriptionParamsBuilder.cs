// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace EventStore.Core.Services.PersistentSubscription;

/// <summary>
/// Builds a <see cref="PersistentSubscriptionParams"/> object.
/// </summary>
public abstract class PersistentSubscriptionParamsBuilder {
	private bool _resolveLinkTos;
	private IPersistentSubscriptionStreamPosition _startFrom;
	private bool _recordStatistics;
	private TimeSpan _timeout;
	private int _readBatchSize;
	private int _maxRetryCount;
	private int _liveBufferSize;
	private int _historyBufferSize;
	private string _subscriptionId;
	private IPersistentSubscriptionEventSource _eventSource;
	private string _groupName;
	private IPersistentSubscriptionStreamReader _streamReader;
	private IPersistentSubscriptionCheckpointReader _checkpointReader;
	private IPersistentSubscriptionCheckpointWriter _checkpointWriter;
	private IPersistentSubscriptionMessageParker _messageParker;
	private TimeSpan _checkPointAfter;
	private int _minCheckPointCount;
	private int _maxCheckPointCount;
	private int _maxSubscriberCount;
	private IPersistentSubscriptionConsumerStrategy _consumerStrategy;

	/// <summary>
	/// Sets the checkpoint reader for the instance
	/// </summary>
	/// <param name="reader"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder
		WithCheckpointReader(IPersistentSubscriptionCheckpointReader reader) {
		_checkpointReader = reader;
		return this;
	}

	/// <summary>
	/// Sets the group that the subscription belongs to
	/// </summary>
	/// <param name="groupName"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder SetGroup(string groupName) {
		_groupName = groupName;
		return this;
	}

	/// <summary>
	/// Sets the unique subscription id of the subscription
	/// </summary>
	/// <param name="subscriptionId"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder SetSubscriptionId(string subscriptionId) {
		_subscriptionId = subscriptionId;
		return this;
	}

	/// <summary>
	/// Sets the event source of the subscription
	/// </summary>
	/// <param name="eventSource"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder WithEventSource(IPersistentSubscriptionEventSource eventSource) {
		_eventSource = eventSource;
		return this;
	}

	/// <summary>
	/// Sets the message parker for the instance
	/// </summary>
	/// <param name="parker"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder WithMessageParker(IPersistentSubscriptionMessageParker parker) {
		_messageParker = parker;
		return this;
	}

	/// <summary>
	/// Sets the check point reader for the subscription
	/// </summary>
	/// <param name="writer"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder
		WithCheckpointWriter(IPersistentSubscriptionCheckpointWriter writer) {
		_checkpointWriter = writer;
		return this;
	}

	/// <summary>
	/// Sets the event loader for the subscription
	/// </summary>
	/// <param name="loader"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder WithEventLoader(IPersistentSubscriptionStreamReader loader) {
		_streamReader = loader;
		return this;
	}

	/// <summary>
	/// Sets the option to include further latency statistics. These statistics have a cost and should not be used
	/// in high performance situations.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithExtraStatistics() {
		_recordStatistics = true;
		return this;
	}

	/// <summary>
	/// Sets the option to resolve linktos on events that are found for this subscription.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder ResolveLinkTos() {
		_resolveLinkTos = true;
		return this;
	}

	/// <summary>
	/// Sets the option to not resolve linktos on events that are found for this subscription.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder DoNotResolveLinkTos() {
		_resolveLinkTos = false;
		return this;
	}

	/// <summary>
	/// If set the subscription will prefer if possible to round robin between the clients that
	/// are connected.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder PreferRoundRobin() {
		_consumerStrategy = new RoundRobinPersistentSubscriptionConsumerStrategy();
		return this;
	}

	/// <summary>
	/// If set the subscription will prefer if possible to dispatch only to a single of the connected
	/// clients. If however the buffer limits are reached on that client it will begin sending to other 
	/// clients.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder PreferDispatchToSingle() {
		_consumerStrategy = new DispatchToSinglePersistentSubscriptionConsumerStrategy();
		return this;
	}

	/// <summary>
	/// Sets the consumer strategy to the one provided.
	/// </summary>
	/// <param name="consumerStrategy"></param>
	/// <returns></returns>
	public PersistentSubscriptionParamsBuilder CustomConsumerStrategy(
		IPersistentSubscriptionConsumerStrategy consumerStrategy) {
		_consumerStrategy = consumerStrategy;
		return this;
	}

	/// <summary>
	/// Sets that the subscription should start from a specified location.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder StartFrom(IPersistentSubscriptionStreamPosition startFrom) {
		_startFrom = startFrom;
		return this;
	}

	public abstract PersistentSubscriptionParamsBuilder StartFromBeginning();

	public abstract PersistentSubscriptionParamsBuilder StartFromCurrent();

	/// <summary>
	/// Sets the timeout timespan to about 30k years.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder DontTimeoutMessages() {
		_timeout = TimeSpan.MaxValue;
		return this;
	}

	/// <summary>
	/// Sets the time after which the subscription should be checkpointed
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder CheckPointAfter(TimeSpan time) {
		_checkPointAfter = time;
		return this;
	}

	/// <summary>
	/// Sets the minimum number of items to checkpoint
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder MinimumToCheckPoint(int count) {
		_minCheckPointCount = count;
		return this;
	}

	/// <summary>
	/// Sets the maximum number of items to checkpoint
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder MaximumToCheckPoint(int count) {
		_maxCheckPointCount = count;
		return this;
	}

	/// <summary>
	/// Sets the maximum number of subscribers
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder MaximumSubscribers(int count) {
		_maxSubscriberCount = count;
		return this;
	}

	/// <summary>
	/// Sets the timeout for a message (will be retried if an ack is not received within this timespan)
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithMessageTimeoutOf(TimeSpan timeout) {
		_timeout = timeout;
		return this;
	}

	/// <summary>
	/// Sets the number of times a message should be retried before being considered a bad message
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithMaxRetriesOf(int count) {
		Ensure.Nonnegative(count, "count");
		_maxRetryCount = count;
		return this;
	}

	/// <summary>
	/// Sets the size of the live buffer for the subscription. This is the buffer used 
	/// to cache messages while sending messages as they happen. The count is
	/// in terms of the number of messages to cache.
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithLiveBufferSizeOf(int count) {
		Ensure.Nonnegative(count, "count");
		_liveBufferSize = count;
		return this;
	}


	/// <summary>
	/// Sets the size of the read batch used when paging in history for the subscription
	/// sizes should not be too big ...
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithReadBatchOf(int count) {
		Ensure.Nonnegative(count, "count");
		_readBatchSize = count;
		return this;
	}


	/// <summary>
	/// Sets the size of the read batch used when paging in history for the subscription
	/// sizes should not be too big ...
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithHistoryBufferSizeOf(int count) {
		Ensure.Nonnegative(count, "count");
		_historyBufferSize = count;
		return this;
	}

	/// <summary>
	/// Sets the size of the read batch used when paging in history for the subscription
	/// sizes should not be too big ...
	/// </summary>
	/// <returns>A new <see cref="PersistentSubscriptionParamsBuilder"></see></returns>
	public PersistentSubscriptionParamsBuilder WithNamedConsumerStrategy(
		IPersistentSubscriptionConsumerStrategy consumerStrategy) {
		Ensure.NotNull(consumerStrategy, "consumerStrategy");
		_consumerStrategy = consumerStrategy;
		return this;
	}

	/// <summary>
	/// Builds a <see cref="PersistentSubscriptionParams"/> object from a <see cref="PersistentSubscriptionParamsBuilder"/>.
	/// </summary>
	/// <param name="builder"><see cref="PersistentSubscriptionParamsBuilder"/> from which to build a <see cref="PersistentSubscriptionParamsBuilder"/></param>
	/// <returns></returns>
	public static implicit operator PersistentSubscriptionParams(PersistentSubscriptionParamsBuilder builder) {
		return new PersistentSubscriptionParams(builder._resolveLinkTos,
			builder._subscriptionId,
			builder._eventSource,
			builder._groupName,
			builder._startFrom,
			builder._recordStatistics,
			builder._timeout,
			builder._maxRetryCount,
			builder._liveBufferSize,
			builder._historyBufferSize,
			builder._readBatchSize,
			builder._checkPointAfter,
			builder._minCheckPointCount,
			builder._maxCheckPointCount,
			builder._maxSubscriberCount,
			builder._consumerStrategy,
			builder._streamReader,
			builder._checkpointReader,
			builder._checkpointWriter,
			builder._messageParker);
	}
}
