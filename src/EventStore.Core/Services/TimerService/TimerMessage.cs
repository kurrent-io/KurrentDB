// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.TimerService;

public static partial class TimerMessage {
	[DerivedMessage(CoreMessage.Timer)]
	public partial class Schedule : Message {
		public readonly TimeSpan TriggerAfter;
		public readonly IEnvelope Envelope;
		public readonly Message ReplyMessage;

		private readonly Action _replyAction;

		public static Schedule Create<T>(TimeSpan triggerAfter, IEnvelope envelope, T replyMessage) where T : Message {
			return new Schedule(triggerAfter, envelope, replyMessage, () => envelope.ReplyWith(replyMessage));
		}

		private Schedule(TimeSpan triggerAfter, IEnvelope envelope, Message replyMessage, Action replyAction) {
			TriggerAfter = triggerAfter;
			Envelope = Ensure.NotNull(envelope);
			ReplyMessage = Ensure.NotNull(replyMessage);
			_replyAction = Ensure.NotNull(replyAction);
		}

		public void Reply() {
			_replyAction();
		}
	}
}
