// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Messages;

public partial class ClientMessage {
	public abstract class ReadIndexEventsCompleted(
		ReadIndexResult result,
		IReadOnlyList<ResolvedEvent> events,
		long tfLastCommitPosition,
		bool isEndOfStream,
		string error
	) : ReadResponseMessage {
		public readonly ReadIndexResult Result = result;
		public IReadOnlyList<ResolvedEvent> Events = events;
		public long TfLastCommitPosition = tfLastCommitPosition;
		public bool IsEndOfStream = isEndOfStream;
		public string Error = error;
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadIndexEventsForward(
		Guid internalCorrId,
		Guid correlationId,
		IEnvelope envelope,
		string indexName,
		long commitPosition,
		long preparePosition,
		bool excludeStart,
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
		public readonly bool ExcludeStart = excludeStart;
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
		ReadIndexResult result,
		IReadOnlyList<ResolvedEvent> events,
		TFPos currentPos,
		long tfLastCommitPosition,
		bool isEndOfStream,
		string error)
		: ReadIndexEventsCompleted(result, events, tfLastCommitPosition, isEndOfStream, error) {
		public readonly TFPos CurrentPos = currentPos;
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadIndexEventsBackward(
		Guid internalCorrId,
		Guid correlationId,
		IEnvelope envelope,
		string indexName,
		long commitPosition,
		long preparePosition,
		bool excludeStart,
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
		public readonly bool ExcludeStart = excludeStart;
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
	public partial class ReadIndexEventsBackwardCompleted(ReadIndexResult result, IReadOnlyList<ResolvedEvent> events, long tfLastCommitPosition, bool isEndOfStream, string error)
		: ReadIndexEventsCompleted(result, events, tfLastCommitPosition, isEndOfStream, error);
}
