// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Messages.EventReaders.Feeds;

public static partial class FeedReaderMessage {
	[DerivedMessage]
	public abstract partial class FeedReaderMessageBase : Message {
	}

	[DerivedMessage(ProjectionMessage.FeedReader)]
	public sealed partial class ReadPage : FeedReaderMessageBase {
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly ClaimsPrincipal User;

		public readonly QuerySourcesDefinition QuerySource;
		public readonly CheckpointTag FromPosition;
		public readonly int MaxEvents;

		public ReadPage(
			Guid correlationId, IEnvelope envelope, ClaimsPrincipal user, QuerySourcesDefinition querySource,
			CheckpointTag fromPosition,
			int maxEvents) {
			User = user;
			CorrelationId = correlationId;
			Envelope = envelope;
			QuerySource = querySource;
			FromPosition = fromPosition;
			MaxEvents = maxEvents;
		}
	}

	[DerivedMessage(ProjectionMessage.FeedReader)]
	public sealed partial class FeedPage : FeedReaderMessageBase {
		public enum ErrorStatus {
			Success,
			NotAuthorized
		}

		public readonly Guid CorrelationId;
		public readonly ErrorStatus Error;
		public readonly TaggedResolvedEvent[] Events;
		public readonly CheckpointTag LastReaderPosition;

		public FeedPage(
			Guid correlationId, ErrorStatus error, TaggedResolvedEvent[] events, CheckpointTag lastReaderPosition) {
			CorrelationId = correlationId;
			Error = error;
			Events = events;
			LastReaderPosition = lastReaderPosition;
		}
	}
}
