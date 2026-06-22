// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Components.PersistentSubscriptions;

public class PersistentSubscriptionsService(IPublisher publisher, IAuthorizationProvider authorizer) {
	static readonly Operation StatisticsOperation = new(Operations.Subscriptions.Statistics);
	static readonly Operation CreateOperation = new(Operations.Subscriptions.Create);
	static readonly Operation UpdateOperation = new(Operations.Subscriptions.Update);
	static readonly Operation DeleteOperation = new(Operations.Subscriptions.Delete);
	static readonly Operation RestartOperation = new(Operations.Subscriptions.Restart);

	static Operation Scoped(OperationDefinition definition, string stream, string group = null) {
		var operation = new Operation(definition).WithParameter(Operations.Subscriptions.Parameters.StreamId(stream));
		return group is null
			? operation
			: operation.WithParameter(Operations.Subscriptions.Parameters.SubscriptionId(group));
	}

	public async ValueTask<(List<MonitoringMessage.PersistentSubscriptionInfo> Items, int Total)> GetPageAsync(
		ClaimsPrincipal principal, int offset, int count, CancellationToken ct) {

		await Authorize(principal, StatisticsOperation, ct);
		var envelope = new TcsEnvelope<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>();
		publisher.Publish(new MonitoringMessage.GetAllPersistentSubscriptionStats(envelope, offset, count));
		var result = await envelope.Task.WaitAsync(ct);
		if (result.Result != MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success)
			return ([], 0);
		return (result.SubscriptionStats ?? [], result.Total);
	}

	public async ValueTask<MonitoringMessage.PersistentSubscriptionInfo> GetDetailAsync(
		ClaimsPrincipal principal, string streamId, string groupName, CancellationToken ct) {

		await Authorize(principal, StatisticsOperation, ct);
		var envelope = new TcsEnvelope<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>();
		publisher.Publish(new MonitoringMessage.GetPersistentSubscriptionStats(envelope, streamId, groupName));
		var result = await envelope.Task.WaitAsync(ct);
		if (result.Result != MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success
			|| result.SubscriptionStats == null || result.SubscriptionStats.Count == 0)
			throw new PersistentSubscriptionsException($"Subscription '{groupName}' on '{streamId}' not found.");
		return result.SubscriptionStats[0];
	}

	public async ValueTask CreateAsync(ClaimsPrincipal principal, string streamId, string groupName,
		SubscriptionConfig config, CancellationToken ct) {

		await Authorize(principal, CreateOperation, ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.CreatePersistentSubscriptionToStream(
			corrId, corrId, envelope, streamId, groupName,
			config.ResolveLinkTos, config.StartFrom, config.MessageTimeoutMilliseconds,
			config.ExtraStatistics, config.MaxRetryCount, config.BufferSize,
			config.LiveBufferSize, config.ReadBatchSize, config.CheckPointAfterMilliseconds,
			config.MinCheckPointCount, config.MaxCheckPointCount, config.MaxSubscriberCount,
			config.NamedConsumerStrategy, principal));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.CreatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.Success }:
				return;
			case ClientMessage.CreatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.AlreadyExists }:
				throw new PersistentSubscriptionsException("Subscription already exists.");
			case ClientMessage.CreatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.CreatePersistentSubscriptionToStreamCompleted.CreatePersistentSubscriptionToStreamResult.AccessDenied }:
				throw new PersistentSubscriptionsException("Access denied.");
			case ClientMessage.CreatePersistentSubscriptionToStreamCompleted failed:
				throw new PersistentSubscriptionsException(
					string.IsNullOrEmpty(failed.Reason)
						? "Failed to create subscription."
						: failed.Reason);
			default:
				throw new PersistentSubscriptionsException("Failed to create subscription.");
		}
	}

	public async ValueTask CreateToAllAsync(ClaimsPrincipal principal, string groupName,
		SubscriptionConfig config, CancellationToken ct) {

		await Authorize(principal, CreateOperation, ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.CreatePersistentSubscriptionToAll(
			corrId, corrId, envelope, groupName,
			eventFilter: null,
			config.ResolveLinkTos,
			new TFPos(config.StartFrom, config.StartFrom),
			config.MessageTimeoutMilliseconds,
			config.ExtraStatistics, config.MaxRetryCount, config.BufferSize,
			config.LiveBufferSize, config.ReadBatchSize, config.CheckPointAfterMilliseconds,
			config.MinCheckPointCount, config.MaxCheckPointCount, config.MaxSubscriberCount,
			config.NamedConsumerStrategy, principal));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.CreatePersistentSubscriptionToAllCompleted { Result: ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult.Success }:
				return;
			case ClientMessage.CreatePersistentSubscriptionToAllCompleted { Result: ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult.AlreadyExists }:
				throw new PersistentSubscriptionsException("Subscription already exists.");
			case ClientMessage.CreatePersistentSubscriptionToAllCompleted { Result: ClientMessage.CreatePersistentSubscriptionToAllCompleted.CreatePersistentSubscriptionToAllResult.AccessDenied }:
				throw new PersistentSubscriptionsException("Access denied.");
			case ClientMessage.CreatePersistentSubscriptionToAllCompleted failed:
				throw new PersistentSubscriptionsException(
					string.IsNullOrEmpty(failed.Reason)
						? "Failed to create subscription."
						: failed.Reason);
			default:
				throw new PersistentSubscriptionsException("Failed to create subscription.");
		}
	}

	public async ValueTask UpdateAsync(ClaimsPrincipal principal, string streamId, string groupName,
		SubscriptionConfig config, CancellationToken ct) {

		await Authorize(principal, UpdateOperation, ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.UpdatePersistentSubscriptionToStream(
			corrId, corrId, envelope, streamId, groupName,
			config.ResolveLinkTos, config.StartFrom, config.MessageTimeoutMilliseconds,
			config.ExtraStatistics, config.MaxRetryCount, config.BufferSize,
			config.LiveBufferSize, config.ReadBatchSize, config.CheckPointAfterMilliseconds,
			config.MinCheckPointCount, config.MaxCheckPointCount, config.MaxSubscriberCount,
			config.NamedConsumerStrategy, principal));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.Success }:
				return;
			case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.DoesNotExist }:
				throw new PersistentSubscriptionsException("Subscription does not exist.");
			case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted { Result: ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult.AccessDenied }:
				throw new PersistentSubscriptionsException("Access denied.");
			case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted failed:
				throw new PersistentSubscriptionsException(
					string.IsNullOrEmpty(failed.Reason)
						? "Failed to update subscription."
						: failed.Reason);
			default:
				throw new PersistentSubscriptionsException("Failed to update subscription.");
		}
	}

	public async ValueTask DeleteAsync(ClaimsPrincipal principal, string streamId, string groupName, CancellationToken ct) {
		await Authorize(principal, DeleteOperation, ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.DeletePersistentSubscriptionToStream(
			corrId, corrId, envelope, streamId, groupName, principal));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.DeletePersistentSubscriptionToStreamCompleted { Result: ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.Success }:
				return;
			case ClientMessage.DeletePersistentSubscriptionToStreamCompleted { Result: ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.DoesNotExist }:
				throw new PersistentSubscriptionsException("Subscription does not exist.");
			case ClientMessage.DeletePersistentSubscriptionToStreamCompleted { Result: ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult.AccessDenied }:
				throw new PersistentSubscriptionsException("Access denied.");
			default:
				throw new PersistentSubscriptionsException("Failed to delete subscription.");
		}
	}

	public async ValueTask ReplayParkedAsync(ClaimsPrincipal principal, string streamId, string groupName, CancellationToken ct) {
		await Authorize(principal, Scoped(Operations.Subscriptions.ReplayParked, streamId, groupName), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.ReplayParkedMessages(
			corrId, corrId, envelope, streamId, groupName, stopAt: null, user: principal));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Success }:
				return;
			case ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.DoesNotExist }:
				throw new PersistentSubscriptionsException("Subscription does not exist.");
			case ClientMessage.ReplayMessagesReceived { Result: ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied }:
				throw new PersistentSubscriptionsException("Access denied.");
			default:
				throw new PersistentSubscriptionsException("Failed to replay parked messages.");
		}
	}

	public async ValueTask<(IReadOnlyList<ResolvedEvent> Events, bool IsEndOfStream, long NextEventNumber)> GetParkedMessagesAsync(
		ClaimsPrincipal principal, string streamId, string groupName, long fromEventNumber, int maxCount, CancellationToken ct) {

		await Authorize(principal, Scoped(Operations.Subscriptions.Statistics, streamId), ct);
		var parkedStreamId = $"$persistentsubscription-{streamId}::{groupName}-parked";
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsBackward(
			corrId, corrId, envelope, parkedStreamId, fromEventNumber, maxCount,
			resolveLinkTos: true, requireLeader: false, validationStreamVersion: null,
			user: principal, replyOnExpired: false));

		var result = await envelope.Task.WaitAsync(ct);

		if (result.Result == ReadStreamResult.AccessDenied)
			throw new PersistentSubscriptionsException("Access denied to parked messages.");
		if (result.Result is not ReadStreamResult.Success)
			return (Array.Empty<ResolvedEvent>(), true, -1);

		return (result.Events, result.IsEndOfStream, result.NextEventNumber);
	}

	public async ValueTask RestartSubsystemAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, RestartOperation, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new SubscriptionMessage.PersistentSubscriptionsRestart(envelope));
		switch (await envelope.Task.WaitAsync(ct)) {
			case SubscriptionMessage.PersistentSubscriptionsRestarting:
				return;
			// e.g. "The Persistent Subscriptions subsystem cannot be restarted because it is not started."
			case SubscriptionMessage.InvalidPersistentSubscriptionsRestart invalid:
				throw new PersistentSubscriptionsException(invalid.Reason);
			default:
				throw new PersistentSubscriptionsException("Failed to restart the persistent subscriptions subsystem.");
		}
	}

	// Enforce the configured authorization policy before publishing, translating a denial into the
	// PersistentSubscriptionsException the components already handle.
	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new PersistentSubscriptionsException("Access denied.");
	}
}

public class SubscriptionConfig {
	public bool ResolveLinkTos { get; set; }
	public long StartFrom { get; set; }
	public int MessageTimeoutMilliseconds { get; set; } = 10000;
	public bool ExtraStatistics { get; set; }
	public int MaxRetryCount { get; set; } = 10;
	public int BufferSize { get; set; } = 500;
	public int LiveBufferSize { get; set; } = 500;
	public int ReadBatchSize { get; set; } = 20;
	public int CheckPointAfterMilliseconds { get; set; } = 1000;
	public int MinCheckPointCount { get; set; } = 10;
	public int MaxCheckPointCount { get; set; } = 500;
	public int MaxSubscriberCount { get; set; } = 10;
	public string NamedConsumerStrategy { get; set; } = "RoundRobin";
}

public class PersistentSubscriptionsException(string message) : Exception(message);
