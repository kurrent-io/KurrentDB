// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.AutoScavenge.Serialization;
using KurrentDB.POC.IO.Core;
using KurrentDB.POC.IO.Core.Serialization;
using Serilog;

namespace KurrentDB.AutoScavenge.Scavengers;

/// Works out the status of a node scavenge from what the node reports about its last scavenge, falling back to the
/// scavenge log when the node no longer knows.
internal static class ScavengeLog {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ScavengeLog));

	public static async Task<ScavengeStatus> GetStatusAsync(
		IClient client,
		Guid scavengeId,
		Guid? lastScavengeId,
		LastScavengeResult lastScavengeResult,
		CancellationToken token) {

		if (lastScavengeResult == LastScavengeResult.Unknown) {
			// the node was restarted. we don't know if our scavenge completed successfully, so we try to read its
			// stream from the scavenge log.
			return await GetStatusFromStream(client, scavengeId, token);
		}

		if (lastScavengeId != scavengeId) {
			// the scavenge ids do not match. this means that a new scavenge was launched, presumably by the user,
			// definitely after the one we started. we just log a warning and continue normally, pretending that
			// this scavenge is the one we started.
			Log.Warning("The last scavenge ID: {lastScavengeId} does not match with the expected scavenge ID: {expectedScavengeId}.",
				lastScavengeId, scavengeId);
		}

		return lastScavengeResult switch {
			LastScavengeResult.InProgress => ScavengeStatus.InProgress,
			LastScavengeResult.Success => ScavengeStatus.Success,
			LastScavengeResult.Errored => ScavengeStatus.Errored,
			LastScavengeResult.Stopped => ScavengeStatus.Stopped,
			_ => ScavengeStatus.Unknown,
		};
	}

	// this is only called if the node reported that it is not currently scavenging and has not scavenged since startup
	private static async Task<ScavengeStatus> GetStatusFromStream(IClient client, Guid scavengeId, CancellationToken token) {
		// it's possible that the `$scavengeCompleted` event is not the last event in the stream as in some cases the
		// events may be out of order. so, we read more than one event to increase the chance of finding it if it is there.
		var events = client
			.ReadStreamBackwards($"$scavenges-{scavengeId}", maxCount: 20, token)
			.HandleStreamNotFound();

		await foreach (var @event in events) {
			if (@event.EventType != "$scavengeCompleted")
				continue;

			var completed =
				JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.ScavengeRecordCompleted)!;

			if (completed.ScavengeId != scavengeId) {
				// ignore, malformed entry
				continue;
			}

			switch (completed.Result) {
				case ScavengeResult.Unknown:
					return ScavengeStatus.NotRunningUnknown;
				case ScavengeResult.Success:
					return ScavengeStatus.Success;
				case ScavengeResult.Stopped:
					return ScavengeStatus.Stopped;
				case ScavengeResult.Errored:
					return ScavengeStatus.Errored;
				case ScavengeResult.Interrupted:
					return ScavengeStatus.NotRunningUnknown;
			}
		}

		// at this point, we cannot know the status of the scavenge for sure:
		// entries may be missing from the scavenge log even if a scavenge had completed, for example:
		// i)   a cluster loses its leader after a scavenge has started on a node. the scavenge continues but
		//      events can no longer be written to the scavenge log.
		// ii)  a network partition separates a node from the cluster after a scavenge started on that node. the node
		//      won't be able to forward scavenge log events to the leader.

		return ScavengeStatus.NotRunningUnknown;
	}

	// Names match the status in the $scavengeCompleted event from the server (apart from Unknown)
	internal enum ScavengeResult {
		Unknown,
		Success,
		Stopped,
		Errored,
		Interrupted,
	}

	internal class ScavengeRecordCompleted {
		public required Guid ScavengeId { get; init; }
		[JsonConverter(typeof(EnumConverterWithDefault<ScavengeResult>))]
		public required ScavengeResult Result { get; init; }
	}
}
