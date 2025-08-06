// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Messages;

public partial class ClientMessage {
	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadIndexEventsForward(
		Guid internalCorrId,
		Guid correlationId,
		IEnvelope envelope,
		string indexName,
		long commitPosition,
		long preparePosition,
		int maxCount,
		bool requireLeader,
		long? validationTfLastCommitPosition,
		ClaimsPrincipal user,
		bool replyOnExpired,
		TimeSpan? longPollTimeout = null,
		DateTime? expires = null,
		CancellationToken cancellationToken = default)
		: ReadRequestMessage(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
		public readonly long CommitPosition = commitPosition;
		public readonly long PreparePosition = preparePosition;
		public readonly int MaxCount = maxCount;
		public readonly bool RequireLeader = requireLeader;
		public readonly bool ReplyOnExpired = replyOnExpired;
		public readonly string IndexName = indexName;
		public readonly long? ValidationTfLastCommitPosition = validationTfLastCommitPosition;
		public readonly TimeSpan? LongPollTimeout = longPollTimeout;

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"IndexName: {IndexName}, " +
			$"PreparePosition: {PreparePosition}, " +
			$"CommitPosition: {CommitPosition}, " +
			$"MaxCount: {MaxCount}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationTfLastCommitPosition: {ValidationTfLastCommitPosition}, " +
			$"LongPollTimeout: {LongPollTimeout}, " +
			$"ReplyOnExpired: {ReplyOnExpired}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadIndexEventsForwardCompleted(
		Guid correlationId,
		ReadIndexResult result,
		[CanBeNull] string error,
		IReadOnlyList<ResolvedEvent> events,
		int maxCount,
		TFPos currentPos,
		TFPos nextPos,
		TFPos prevPos,
		long tfLastCommitPosition,
		bool isEndOfStream)
		: ReadResponseMessage {
		public readonly Guid CorrelationId = correlationId;
		public readonly ReadIndexResult Result = result;
		public readonly string Error = error;
		public readonly IReadOnlyList<ResolvedEvent> Events = Ensure.NotNull(events);
		public readonly int MaxCount = maxCount;
		public readonly TFPos CurrentPos = currentPos;
		public readonly TFPos NextPos = nextPos;
		public readonly TFPos PrevPos = prevPos;
		public readonly long TfLastCommitPosition = tfLastCommitPosition;
		public readonly bool IsEndOfStream = isEndOfStream;
	}
}
