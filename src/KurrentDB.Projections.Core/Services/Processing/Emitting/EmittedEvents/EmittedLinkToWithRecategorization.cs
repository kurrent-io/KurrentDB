// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public class EmittedLinkToWithRecategorization(
	string streamId,
	Guid eventId,
	string target,
	CheckpointTag causedByTag,
	CheckpointTag expectedTag,
	string originalStreamId,
	int? streamDeletedAt)
	: EmittedEvent(streamId, eventId, "$>", causedByTag, expectedTag) {
	public override string Data { get; } = target;

	public override bool IsJson => false;

	public override bool IsReady() => true;

	public override IEnumerable<KeyValuePair<string, string>> ExtraMetaData() {
		if (!string.IsNullOrEmpty(originalStreamId))
			yield return new("$o", JsonConvert.ToString(originalStreamId));
		if (streamDeletedAt != null)
			yield return new("$deleted", JsonConvert.ToString(streamDeletedAt.Value));
	}
}
