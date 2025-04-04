// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.VNode;

public class MessageForwardingProxy {
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

	private readonly ConcurrentDictionary<Guid, Forwarding> _forwardings =
		new ConcurrentDictionary<Guid, Forwarding>();

	public void Register(Guid internalCorrId, Guid clientCorrId, IEnvelope envelope, TimeSpan timeout,
		Message timeoutMessage) {
		_forwardings.AddOrUpdate(
			internalCorrId,
			new Forwarding(clientCorrId, envelope, _stopwatch.Elapsed + timeout, timeoutMessage),
			(x, y) => {
				throw new Exception(
					string.Format("Forwarding for InternalCorrId {0:B} (ClientCorrId {1:B}) already exists.",
						internalCorrId, clientCorrId));
			});
	}

	public bool TryForwardReply<TMessage>(Guid correlationId, TMessage originalMessage,
		Func<Guid, TMessage, TMessage> getForwardMessage)
		where TMessage : Message {
		Forwarding forwarding;
		if (_forwardings.TryRemove(correlationId, out forwarding)) {
			forwarding.Envelope.ReplyWith(getForwardMessage(forwarding.ClientCorrId, originalMessage));
			return true;
		}

		return false;
	}

	public void TimeoutForwardings() {
		var now = _stopwatch.Elapsed;

		foreach (var forwPair in _forwardings) {
			Forwarding forwarding;
			if (forwPair.Value.TimeoutTimestamp <= now && _forwardings.TryRemove(forwPair.Key, out forwarding))
				forwarding.Envelope.ReplyWith(forwarding.TimeoutMessage);
		}
	}

	private class Forwarding {
		public readonly Guid ClientCorrId;
		public readonly IEnvelope Envelope;
		public readonly TimeSpan TimeoutTimestamp;
		public readonly Message TimeoutMessage;

		public Forwarding(Guid clientCorrId, IEnvelope envelope, TimeSpan timeoutTimestamp,
			Message timeoutMessage) {
			Ensure.NotNull(envelope, "envelope");
			Ensure.NotNull(timeoutMessage, "timeoutMessage");

			ClientCorrId = clientCorrId;
			Envelope = envelope;
			TimeoutTimestamp = timeoutTimestamp;
			TimeoutMessage = timeoutMessage;
		}
	}
}
