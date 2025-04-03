// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.Services.Storage.Scavenge.ScavengeLogManager;

public static class ScavengerLogHelper {
	public static string ScavengeStreamId(Guid scavengeId) {
		return $"{SystemStreams.ScavengesStream}-{scavengeId}";
	}

	public static StreamMetadata CreateScavengeMetadata(TimeSpan scavengeHistoryMaxAge) {
		return new StreamMetadata(maxAge: scavengeHistoryMaxAge, acl: new(
			new[] { "$ops" },
			new string[] { },
			new string[] { },
			new string[] { },
			new string[] { }
		));
	}

	public static Dictionary<string, object> CreateScavengeStarted(Guid scavengeId, string nodeEndpoint,
		int startFromChunk = 0) {
		return new Dictionary<string, object> {
			{ "scavengeId", scavengeId },
			{ "nodeEndpoint", nodeEndpoint },
			{ "alwaysKeepScavenged", false },
			{ "mergeChunks", true },
			{ "startFromChunk", startFromChunk },
			{ "threads", 1 },
		};
	}

	public static Dictionary<string, object> CreateScavengeInterruptedByRestart
		(Guid scavengeId, string nodeEndpoint, TimeSpan timeTaken, long spaceSaved = 0, int maxChunkScavenged = 0) {
		return new Dictionary<string, object> {
			{ "scavengeId", scavengeId },
			{ "nodeEndpoint", nodeEndpoint },
			{ "result", ScavengeResult.Interrupted },
			{ "error", "The node was restarted." },
			{ "timeTaken", timeTaken },
			{ "spaceSaved", spaceSaved },
			{ "maxChunkScavenged", maxChunkScavenged }
		};
	}

	public static Dictionary<string, object> CreateScavengeChunkCompleted
		(Guid scavengeId, string nodeEndpoint, int chunkStart, int chunkEnd, TimeSpan elapsed, long spaceSaved) {
		return new Dictionary<string, object> {
			{ "scavengeId", scavengeId },
			{ "chunkStartNumber", chunkStart },
			{ "chunkEndNumber", chunkEnd },
			{ "timeTaken", elapsed },
			{ "wasScavenged", true },
			{ "spaceSaved", spaceSaved },
			{ "nodeEndpoint", nodeEndpoint },
			{ "errorMessage", "" }
		};
	}

	public static Dictionary<string, object> CreateScavengeCompletedSuccessfully(
		Guid scavengeId, string nodeEndpoint, TimeSpan elapsed, long spaceSaved, int maxChunkScavenged) {
		return new Dictionary<string, object> {
			{ "scavengeId", scavengeId },
			{ "nodeEndpoint", nodeEndpoint },
			{ "result", ScavengeResult.Success },
			{ "error", "" },
			{ "timeTaken", elapsed },
			{ "spaceSaved", spaceSaved },
			{ "maxChunkScavenged", maxChunkScavenged }
		};
	}
}
