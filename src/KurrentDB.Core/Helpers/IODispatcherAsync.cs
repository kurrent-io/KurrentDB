// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;

namespace KurrentDB.Core.Helpers;

public static class IODispatcherAsync {
	public class CancellationScope {
		private bool _cancelled = false;
		private readonly HashSet<Guid> _ids = new HashSet<Guid>();

		public Guid Register(Guid id) {
			_ids.Add(id);
			return id;
		}

		public bool Cancelled(Guid id) {
			_ids.Remove(id);
			return _cancelled;
		}

		public void Cancel() {
			_cancelled = true;
		}
	}

	public delegate void Step(IEnumerator<Step> nextSteps);

	public static void Run(this IEnumerable<Step> actions) {
		var actionsEnumerator = actions.GetEnumerator();
		Run(actionsEnumerator);
	}

	public static void Run(this Step action) {
		Run(new[] { action });
	}

	public static Step BeginReadForward(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsForwardCompleted> handler,
		Action timeoutHandler) {
		return
			steps => {
				var corrId = Guid.NewGuid();
				cancellationScope.Register(corrId);
				ioDispatcher.ReadForward(
					streamId,
					fromEventNumber,
					maxCount,
					resolveLinks,
					principal,
					response => {
						if (cancellationScope.Cancelled(response.CorrelationId))
							return;
						handler(response);
						Run(steps);
					},
					() => {
						if (cancellationScope.Cancelled(corrId))
							return;
						timeoutHandler();
						Run(steps);
					},
					corrId);
			};
	}

	public static Step BeginReadBackward(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted> handler,
		Action timeoutHandler) {
		return
			steps => {
				var corrId = Guid.NewGuid();
				cancellationScope.Register(corrId);
				ioDispatcher.ReadBackward(
					streamId,
					fromEventNumber,
					maxCount,
					resolveLinks,
					principal,
					response => {
						if (cancellationScope.Cancelled(response.CorrelationId))
							return;
						handler(response);
						Run(steps);
					},
					() => {
						if (cancellationScope.Cancelled(corrId))
							return;
						timeoutHandler();
						Run(steps);
					},
					corrId);
			};
	}

	public static Step BeginWriteEvents(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		ClaimsPrincipal principal,
		Event[] events,
		Action<ClientMessage.WriteEventsCompleted> handler) {
		return
			steps =>
				WriteEventsWithRetry(ioDispatcher, cancellationScope, streamId, expectedVersion, principal, events,
					handler, steps);
	}

	public static Step BeginDeleteStream(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		bool hardDelete,
		ClaimsPrincipal principal,
		Action<ClientMessage.DeleteStreamCompleted> handler) {
		return
			steps =>
				DeleteStreamWithRetry(
					ioDispatcher,
					cancellationScope,
					streamId,
					expectedVersion,
					hardDelete,
					principal,
					handler,
					steps);
	}

	public static Step BeginSubscribeAwake(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		TFPos from,
		Action<IODispatcherDelayedMessage> handler,
		Guid? correlationId = null) {
		return steps => ioDispatcher.SubscribeAwake(
			streamId,
			@from,
			message => {
				if (cancellationScope.Cancelled(message.CorrelationId))
					return;
				handler(message);
				Run(steps);
			},
			correlationId);
	}

	public static Step BeginUpdateStreamAcl(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		ClaimsPrincipal principal,
		StreamMetadata metadata,
		Action<ClientMessage.WriteEventsCompleted> handler) {
		return
			steps =>
				UpdateStreamAclWithRetry(
					ioDispatcher,
					cancellationScope,
					streamId,
					expectedVersion,
					principal,
					metadata,
					handler,
					steps);
	}

	public static Step BeginDelay(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		TimeSpan timeout,
		Action handler) {
		return steps => ioDispatcher.Delay(
			timeout,
			_ => {
				if (cancellationScope.Cancelled(Guid.Empty))
					return;
				handler();
				Run(steps);
			});
	}

	private static void WriteEventsWithRetry(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		ClaimsPrincipal principal,
		Event[] events,
		Action<ClientMessage.WriteEventsCompleted> handler,
		IEnumerator<Step> steps) {
		PerformWithRetry(
			ioDispatcher,
			handler,
			steps,
			expectedVersion == ExpectedVersion.Any,
			TimeSpan.FromMilliseconds(100),
			action =>
				cancellationScope.Register(
					ioDispatcher.WriteEvents(
						streamId,
						expectedVersion,
						events,
						principal,
						response => {
							if (cancellationScope.Cancelled(response.CorrelationId))
								return;
							action(response, response.Result);
						})));
	}

	private static void DeleteStreamWithRetry(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		bool hardDelete,
		ClaimsPrincipal principal,
		Action<ClientMessage.DeleteStreamCompleted> handler,
		IEnumerator<Step> steps) {
		PerformWithRetry(
			ioDispatcher,
			handler,
			steps,
			expectedVersion == ExpectedVersion.Any,
			TimeSpan.FromMilliseconds(100),
			action =>
				cancellationScope.Register(
					ioDispatcher.DeleteStream(
						streamId,
						expectedVersion,
						hardDelete,
						principal,
						response => {
							if (cancellationScope.Cancelled(response.CorrelationId))
								return;
							action(response, response.Result);
						})));
	}


	private static void UpdateStreamAclWithRetry(
		this IODispatcher ioDispatcher,
		CancellationScope cancellationScope,
		string streamId,
		long expectedVersion,
		ClaimsPrincipal principal,
		StreamMetadata metadata,
		Action<ClientMessage.WriteEventsCompleted> handler,
		IEnumerator<Step> steps) {
		PerformWithRetry(
			ioDispatcher,
			handler,
			steps,
			expectedVersion == ExpectedVersion.Any,
			TimeSpan.FromMilliseconds(100),
			action =>
				cancellationScope.Register(
					ioDispatcher.WriteEvents(
						SystemStreams.MetastreamOf(streamId),
						expectedVersion,
						new[] {
							new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, true, metadata.ToJsonBytes(),
								null, SchemaInfo.None, SchemaInfo.None)
						},
						principal,
						response => {
							if (cancellationScope.Cancelled(response.CorrelationId))
								return;
							action(response, response.Result);
						})));
	}

	private static void PerformWithRetry<T>(
		this IODispatcher ioDispatcher,
		Action<T> handler,
		IEnumerator<Step> steps,
		bool retryExpectedVersion,
		TimeSpan timeout,
		Action<Action<T, OperationResult>> action) {
		action(
			(response, result) => {
				if (ShouldRetry(result, retryExpectedVersion)) {
					ioDispatcher.Delay(
						timeout,
						_ => {
							if (timeout < TimeSpan.FromSeconds(10))
								timeout += timeout;
							PerformWithRetry(ioDispatcher, handler, steps, retryExpectedVersion, timeout, action);
						});
				} else {
					handler(response);
					Run(steps);
				}
			});
	}

	private static bool ShouldRetry(OperationResult result, bool retryExpectedVersion) {
		switch (result) {
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout:
			case OperationResult.PrepareTimeout:
				return true;
			case OperationResult.WrongExpectedVersion:
				return retryExpectedVersion;
			default:
				return false;
		}
	}

	private static void Run(IEnumerator<Step> actions) {
		if (actions.MoveNext()) {
			var action = actions.Current;
			action(actions);
		}
	}
}
