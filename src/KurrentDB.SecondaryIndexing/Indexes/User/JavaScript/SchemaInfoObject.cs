// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Jint.Native.Json;
using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

internal sealed class SchemaInfoObject(Engine engine, JsonParser parser) : JsObject(engine, parser) {
	private string Subject {
		set => SetReadOnlyProperty("subject", value);
	}

	private new string Type {
		set => SetReadOnlyProperty("type", value);
	}

	public void MapFrom(ResolvedEvent resolvedEvent, ulong sequenceId) {
		Subject = resolvedEvent.OriginalEvent.EventType;
		Type = resolvedEvent.OriginalEvent.SchemaFormat;
	}
}
