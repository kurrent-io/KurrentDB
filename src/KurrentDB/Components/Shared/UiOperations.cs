// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using KurrentDB.Core.Services;

namespace KurrentDB.Components.Shared;

// Builders for the KurrentDB Operations the UI authorizes against — the same Operations (with the same
// resource parameters) the gRPC/HTTP front-ends use. Centralised so the construction lives in one place.
public static class UiOperations {
	public static Operation ReadStream(string streamId) =>
		new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	public static Operation WriteStream(string streamId) =>
		new Operation(Operations.Streams.Write).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	public static Operation DeleteStream(string streamId) =>
		new Operation(Operations.Streams.Delete).WithParameter(Operations.Streams.Parameters.StreamId(streamId));

	// Read of $all — a cross-stream read (query engine, stats, $all browsing).
	public static Operation ReadAll { get; } = ReadStream(SystemStreams.AllStream);
}
