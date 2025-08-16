// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Messages;

public partial class ClientMessage {
	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadLogEvents(
		Guid internalCorrId,
		Guid correlationId,
		IEnvelope envelope,
		long[] logPositions,
		ClaimsPrincipal user,
		DateTime? expires,
		CancellationToken cancellationToken = default)
		: ReadRequestMessage(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
		public long[] LogPositions = logPositions;
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadLogEventsCompleted(Guid correlationId, ReadEventResult result, ResolvedEvent[] records, string error)
		: ReadResponseMessage {
		public readonly Guid CorrelationId = correlationId;
		public readonly ReadEventResult Result = result;
		public readonly ResolvedEvent[] Records = records;
		public readonly string Error = error;
	}
}
