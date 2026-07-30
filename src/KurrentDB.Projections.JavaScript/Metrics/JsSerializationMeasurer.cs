// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Jint.Native;
using Jint.Native.Json;
using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public class JsSerializationMeasurer(IProjectionStateSerializationTracker tracker) {
	// The serializer is engine-bound and this measurer is constructed before the handler builds its
	// engine, so it is passed in per call, the same way JsFunctionCallMeasurer takes the function it
	// is timing. A JsonSerializer is reusable across calls -- it clears its own per-call state -- so
	// the caller holds one for the life of the handler.
	public string Serialize(JsonSerializer serializer, JsValue value) {
		using var measurer = new Measurer(tracker);

		// Jint returns undefined for the values that have no JSON representation at all: undefined
		// itself, and functions. Callers here treat the result as a JSON document, and the serializer
		// this replaces wrote "null" for those, so keep that mapping rather than returning null.
		return serializer.Serialize(value) is JsString json ? json.ToString() : "null";
	}

	readonly struct Measurer(IProjectionStateSerializationTracker tracker) : IDisposable {
		readonly Instant _start = Instant.Now;

		public void Dispose() {
			tracker.StateSerialized(_start);
		}
	}
}
