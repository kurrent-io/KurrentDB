// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.AwakeReaderService;

public partial class AwakeServiceMessage {
	[DerivedMessage(CoreMessage.Awake)]
	public sealed partial class SubscribeAwake(
		IEnvelope envelope,
		Guid correlationId,
		string streamId,
		TFPos from,
		Message replyWithMessage) : Message {

		public readonly IEnvelope Envelope = envelope;
		public readonly Guid CorrelationId = correlationId;
		public readonly string StreamId = streamId;
		public readonly TFPos From = from;
		public readonly Message ReplyWithMessage = replyWithMessage;
	}

	[DerivedMessage(CoreMessage.Awake)]
	public sealed partial class UnsubscribeAwake(Guid correlationId) : Message {
		public readonly Guid CorrelationId = correlationId;
	}
}
