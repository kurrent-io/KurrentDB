// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Auth.StreamPolicyPlugin.Schema;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using Serilog;

namespace KurrentDB.Auth.StreamPolicyPlugin;

public interface IPolicyWriter {
	public Task WriteDefaultPolicy(Policy policy, CancellationToken ct);
}

// TODO: use the IClient interface instead to write to streams
public class StreamPolicyWriter : IPolicyWriter {
	private readonly IPublisher _bus;
	private readonly string _policyStream;
	private readonly string _policyEventType;
	private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

	private static readonly ILogger Logger = Log.ForContext<StreamBasedPolicySelector>();
	private readonly Func<Policy, byte[]> _serializePolicy;
	public const string DefaultStreamPolicyEventId = "ff8b943c-60f5-49af-845c-ab7150be91d0";

	public StreamPolicyWriter(
		IPublisher bus, string policyStream, string policyEventType, Func<Policy, byte[]> serializePolicy) {
		_bus = bus;
		_policyStream = policyStream;
		_policyEventType = policyEventType;
		_serializePolicy = serializePolicy;
	}

	public Task WriteDefaultPolicy(Policy defaultPolicy, CancellationToken ct) {
		var defaultId = Guid.Parse(DefaultStreamPolicyEventId);
		return WritePolicyWithRetry(defaultPolicy, defaultId, ExpectedVersion.NoStream, ct);
	}

	private async Task WritePolicyWithRetry(Policy policy, Guid eventId, long expectedVersion, CancellationToken ct) {
		var policyEvent = new Event(eventId, _policyEventType, isJson: true, _serializePolicy(policy), [], SchemaInfo.None, SchemaInfo.None);
		var attempt = 0;
		while (true) {
			try {
				if (attempt > 0) {
					await Task.Delay(_retryDelay, ct);
					Logger.Debug(
						"Retrying to write default policy to stream {stream} with expected version {expectedVersion}. Attempt {attempt}",
						_policyStream, expectedVersion, attempt + 1);
				}

				var result = await WriteEvents(_policyStream, [policyEvent], expectedVersion, ct);
				if (result is RetryAction.None or RetryAction.NoRetry)
					return;

			} catch (Exception ex) {
				Logger.Error(ex, "Failed to write default policy to stream {stream}", _policyStream);
			}

			attempt++;
		}
	}

	private async Task<RetryAction> WriteEvents(string streamName, Event[] events, long expectedVersion,
		CancellationToken ct) {
		var correlationId = Guid.NewGuid();
		var appendResponseSource =
			new TaskCompletionSource<RetryAction>(TaskCreationOptions.RunContinuationsAsynchronously);
		var envelope = new CallbackEnvelope(HandleWriteEventsCallback);

		_bus.Publish(new ClientMessage.WriteEvents(
			correlationId,
			correlationId,
			envelope,
			requireLeader: false,
			streamName,
			expectedVersion,
			events,
			SystemAccounts.System,
			cancellationToken: ct));

		return await appendResponseSource.Task;


		void HandleWriteEventsCallback(Message message) {
			if (message is ClientMessage.NotHandled notHandled) {
				appendResponseSource.TrySetResult(HandleNotHandled(notHandled));
				return;
			}

			if (!(message is ClientMessage.WriteEventsCompleted completed)) {
				Logger.Error("Unknown response {response}. Expected {expected}", message.GetType().Name,
					nameof(ClientMessage.WriteEventsCompleted));
				appendResponseSource.TrySetResult(RetryAction.Retry);
				return;
			}

			appendResponseSource.TrySetResult(HandleWriteEventsCompleted(completed, expectedVersion));
		}
	}

	private RetryAction HandleWriteEventsCompleted(ClientMessage.WriteEventsCompleted completed, long expectedVersion) {
		switch (completed.Result) {
			case OperationResult.Success:
				Logger.Debug("Successfully wrote default policy to stream {stream}.", _policyStream);
				return RetryAction.NoRetry;
			case OperationResult.PrepareTimeout:
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout:
				Logger.Error("Timed out while writing default policy to stream {stream}", _policyStream);
				return RetryAction.Retry;
			case OperationResult.StreamDeleted:
				Logger.Error(
					"Could not write default policy to stream {stream} because it has been deleted", _policyStream);
				return RetryAction.NoRetry;
			case OperationResult.WrongExpectedVersion:
				Logger.Debug(
					"A policy event already exists in the stream {stream}. Skipping write of the default policy.",
					_policyStream);
				return RetryAction.NoRetry;
			case OperationResult.AccessDenied:
			case OperationResult.InvalidTransaction:
			default:
				Logger.Error("Failed to write default policy to stream {stream} because of unexpected result: {result}",
					_policyStream, completed.Result);
				return RetryAction.Retry;
		}
	}

	private RetryAction HandleNotHandled(ClientMessage.NotHandled notHandled) {
		switch (notHandled.Reason) {
			case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly:
				Logger.Debug("Could not write default policy to stream {stream} as we are readonly",
					_policyStream);
				return RetryAction.NoRetry;
			case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader:
				Logger.Debug("Could not write default policy to stream {stream} as we are not leader",
					_policyStream);
				return RetryAction.Retry;
			case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
				Logger.Debug("Could not write default policy to stream {stream} as the server is too busy",
					_policyStream);
				return RetryAction.Retry;
			case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
				Logger.Debug("Could not write default policy to stream {stream} as the server is not ready",
					_policyStream);
				return RetryAction.Retry;
		}

		return RetryAction.None;
	}

	private enum RetryAction {
		None = 0,
		NoRetry = 1,
		Retry = 2,
	}
}
