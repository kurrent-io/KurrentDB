// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation GetInfoOperation = new Operation(Plugins.Authorization.Operations.Subscriptions.Statistics);
	public override async Task<GetInfoResp> GetInfo(GetInfoReq request, ServerCallContext context) {
		var getPersistentSubscriptionInfoSource = new TaskCompletionSource<GetInfoResp>();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user,
			GetInfoOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		string streamId = request.Options.StreamOptionCase switch {
			GetInfoReq.Types.Options.StreamOptionOneofCase.All => "$all",
			GetInfoReq.Types.Options.StreamOptionOneofCase.StreamIdentifier => request.Options.StreamIdentifier,
			_ => throw new InvalidOperationException()
		};

		_publisher.Publish(new MonitoringMessage.GetPersistentSubscriptionStats(
			new CallbackEnvelope(HandleGetPersistentSubscriptionStatsCompleted),
			streamId,
			request.Options.GroupName));
		return await getPersistentSubscriptionInfoSource.Task;

		void HandleGetPersistentSubscriptionStatsCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled && RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
				getPersistentSubscriptionInfoSource.TrySetException(ex);
				return;
			}

			if (message is MonitoringMessage.GetPersistentSubscriptionStatsCompleted completed) {
				switch (completed.Result) {
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success:
						var getInfoResp = new GetInfoResp {
							SubscriptionInfo = ParseSubscriptionInfo(completed.SubscriptionStats.First())
						};
						getPersistentSubscriptionInfoSource.TrySetResult(getInfoResp);
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotFound:
						getPersistentSubscriptionInfoSource.TrySetException(
							RpcExceptions.PersistentSubscriptionDoesNotExist(streamId,
								request.Options.GroupName));
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotReady:
						getPersistentSubscriptionInfoSource.TrySetException(
							RpcExceptions.ServerNotReady());
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Fail:
						getPersistentSubscriptionInfoSource.TrySetException(
							RpcExceptions.PersistentSubscriptionFailed(streamId, request.Options.GroupName,
								completed.ErrorString));
						return;
					default:
						getPersistentSubscriptionInfoSource.TrySetException(
							RpcExceptions.UnknownError(completed.Result));
						return;
				}
			}
			getPersistentSubscriptionInfoSource.TrySetException(
				RpcExceptions.UnknownMessage<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>(
					message));
		}
	}

	public override async Task<ListResp> List(ListReq request, ServerCallContext context) {
		var listPersistentSubscriptionsSource = new TaskCompletionSource<ListResp>();
		var correlationId = Guid.NewGuid();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user,
			GetInfoOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var streamId = string.Empty;
		switch (request.Options.ListOptionCase) {
			case ListReq.Types.Options.ListOptionOneofCase.ListAllSubscriptions:
				_publisher.Publish(new MonitoringMessage.GetAllPersistentSubscriptionStats(
					new CallbackEnvelope(HandleListSubscriptionsCompleted)));
				break;
			case ListReq.Types.Options.ListOptionOneofCase.ListForStream:
				streamId = request.Options.ListForStream.StreamOptionCase switch {
					ListReq.Types.StreamOption.StreamOptionOneofCase.All => "$all",
					ListReq.Types.StreamOption.StreamOptionOneofCase.Stream => request.Options.ListForStream.Stream,
					_ => throw new InvalidOperationException()
				};
				_publisher.Publish(new MonitoringMessage.GetStreamPersistentSubscriptionStats(
					new CallbackEnvelope(HandleListSubscriptionsCompleted),
					streamId
				));
				break;
			default:
				throw new InvalidOperationException();
		}

		return await listPersistentSubscriptionsSource.Task;

		void HandleListSubscriptionsCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled && RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
				listPersistentSubscriptionsSource.TrySetException(ex);
				return;
			}

			if (message is MonitoringMessage.GetPersistentSubscriptionStatsCompleted completed) {
				switch (completed.Result) {
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success:
						var listResp = new ListResp();
						listResp.Subscriptions.AddRange(
							completed.SubscriptionStats.Select(ParseSubscriptionInfo)
						);
						listPersistentSubscriptionsSource.TrySetResult(listResp);
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotFound:
						listPersistentSubscriptionsSource.TrySetException(
							RpcExceptions.PersistentSubscriptionDoesNotExist(streamId, ""));
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotReady:
						listPersistentSubscriptionsSource.TrySetException(
							RpcExceptions.ServerNotReady());
						return;
					case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Fail:
						listPersistentSubscriptionsSource.TrySetException(
							RpcExceptions.PersistentSubscriptionFailed(streamId, "", completed.ErrorString));
						return;
					default:
						listPersistentSubscriptionsSource.TrySetException(
							RpcExceptions.UnknownError(completed.Result));
						return;
				}
			}
			listPersistentSubscriptionsSource.TrySetException(
				RpcExceptions.UnknownMessage<MonitoringMessage.GetPersistentSubscriptionStatsCompleted>(
					message));
		}
	}

	private SubscriptionInfo ParseSubscriptionInfo(MonitoringMessage.PersistentSubscriptionInfo input) {
		var connectionInfo = new List<SubscriptionInfo.Types.ConnectionInfo>();
		foreach (var conn in input.Connections) {
			var connInfo = new SubscriptionInfo.Types.ConnectionInfo {
				From = conn.From,
				Username = conn.Username,
				AverageItemsPerSecond = conn.AverageItemsPerSecond,
				TotalItems = conn.TotalItems,
				CountSinceLastMeasurement = conn.CountSinceLastMeasurement,
				AvailableSlots = conn.AvailableSlots,
				InFlightMessages = conn.InFlightMessages,
				ConnectionName = conn.ConnectionName
			};
			connInfo.ObservedMeasurements.AddRange(
				conn.ObservedMeasurements.Select(x => new SubscriptionInfo.Types.Measurement { Key = x.Key, Value = x.Value }));
			connectionInfo.Add(connInfo);
		}

		var subscriptionInfo = new SubscriptionInfo {
			EventSource = input.EventSource,
			GroupName = input.GroupName,
			Status = input.Status,
			AveragePerSecond = input.AveragePerSecond,
			TotalItems = input.TotalItems,
			CountSinceLastMeasurement = input.CountSinceLastMeasurement,
			LastCheckpointedEventPosition = input.LastCheckpointedEventPosition ?? string.Empty,
			LastKnownEventPosition = input.LastKnownEventPosition ?? string.Empty,
			ResolveLinkTos = input.ResolveLinktos,
			StartFrom = input.StartFrom,
			MessageTimeoutMilliseconds = input.MessageTimeoutMilliseconds,
			ExtraStatistics = input.ExtraStatistics,
			MaxRetryCount = input.MaxRetryCount,
			LiveBufferSize = input.LiveBufferSize,
			BufferSize = input.BufferSize,
			ReadBatchSize = input.ReadBatchSize,
			CheckPointAfterMilliseconds = input.CheckPointAfterMilliseconds,
			MinCheckPointCount = input.MinCheckPointCount,
			MaxCheckPointCount = input.MaxCheckPointCount,
			ReadBufferCount = input.ReadBufferCount,
			LiveBufferCount = input.LiveBufferCount,
			RetryBufferCount = input.RetryBufferCount,
			TotalInFlightMessages = input.TotalInFlightMessages,
			OutstandingMessagesCount = input.OutstandingMessagesCount,
			NamedConsumerStrategy = input.NamedConsumerStrategy,
			MaxSubscriberCount = input.MaxSubscriberCount,
			ParkedMessageCount = input.ParkedMessageCount,
		};
		subscriptionInfo.Connections.AddRange(connectionInfo);
		return subscriptionInfo;
	}
}
