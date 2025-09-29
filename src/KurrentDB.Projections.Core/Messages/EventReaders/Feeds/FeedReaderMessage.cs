// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages.EventReaders.Feeds;

public static partial class FeedReaderMessage {
	[DerivedMessage]
	public abstract partial class FeedReaderMessageBase : Message;

	[DerivedMessage(ProjectionMessage.FeedReader)]
	public sealed partial class ReadPage(
		Guid correlationId,
		IEnvelope envelope,
		ClaimsPrincipal user,
		QuerySourcesDefinition querySource,
		CheckpointTag fromPosition,
		int maxEvents)
		: FeedReaderMessageBase {
		public readonly Guid CorrelationId = correlationId;
		public readonly IEnvelope Envelope = envelope;
		public readonly ClaimsPrincipal User = user;

		public readonly QuerySourcesDefinition QuerySource = querySource;
		public readonly CheckpointTag FromPosition = fromPosition;
		public readonly int MaxEvents = maxEvents;
	}

	[DerivedMessage(ProjectionMessage.FeedReader)]
	public sealed partial class FeedPage(
		Guid correlationId,
		FeedPage.ErrorStatus error,
		TaggedResolvedEvent[] events,
		CheckpointTag lastReaderPosition)
		: FeedReaderMessageBase {
		public enum ErrorStatus {
			Success,
			NotAuthorized
		}

		public readonly Guid CorrelationId = correlationId;
		public readonly ErrorStatus Error = error;
		public readonly TaggedResolvedEvent[] Events = events;
		public readonly CheckpointTag LastReaderPosition = lastReaderPosition;
	}
}
