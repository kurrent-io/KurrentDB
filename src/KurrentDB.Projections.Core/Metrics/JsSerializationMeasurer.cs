// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Jint.Native;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Metrics;

public class JsSerializationMeasurer(IProjectionStateSerializationTracker tracker) {
	private readonly JintProjectionStateHandler.Serializer _serializer = new();

	public ReadOnlySpan<byte> Serialize(JsValue value) {
		using var measurer = new Measurer(tracker);
		return _serializer.Serialize(value).Span;
	}

	private readonly struct Measurer(IProjectionStateSerializationTracker tracker) : IDisposable {
		private readonly Instant _start = Instant.Now;

		public void Dispose() {
			tracker.StateSerialized(_start);
		}
	}
}
