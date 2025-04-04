// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using Newtonsoft.Json;

namespace EventStore.Core.TransactionLog.Scavenging;

public class ScavengeCheckpointJsonPersistence<TStreamId> {
	public enum Version {
		None,
		V0,
	}

	public enum Stage {
		None = 0,
		Accumulating = 1,
		Calculating = 2,
		ExecutingChunks = 3,
		MergingChunks = 4,
		ExecutingIndex = 5,
		Cleaning = 6,
		Done = 7,
	}

	public ScavengePoint ScavengePoint { get; set; }
	public Version SchemaVersion { get; set; }
	public Stage CheckpointStage { get; set; }
	public int? DoneLogicalChunkNumber { get; set; }
	public StreamHandle<TStreamId> DoneStreamHandle { get; set; }

	public ScavengeCheckpoint ToDomain() {
		switch (CheckpointStage) {
			case Stage.Accumulating:
				return new ScavengeCheckpoint.Accumulating(ScavengePoint, DoneLogicalChunkNumber);
			case Stage.Calculating:
				return new ScavengeCheckpoint.Calculating<TStreamId>(ScavengePoint, DoneStreamHandle);
			case Stage.ExecutingChunks:
				return new ScavengeCheckpoint.ExecutingChunks(ScavengePoint, DoneLogicalChunkNumber);
			case Stage.MergingChunks:
				return new ScavengeCheckpoint.MergingChunks(ScavengePoint);
			case Stage.ExecutingIndex:
				return new ScavengeCheckpoint.ExecutingIndex(ScavengePoint);
			case Stage.Cleaning:
				return new ScavengeCheckpoint.Cleaning(ScavengePoint);
			case Stage.Done:
				return new ScavengeCheckpoint.Done(ScavengePoint);
			default:
				throw new ArgumentOutOfRangeException(
					nameof(CheckpointStage),
					CheckpointStage,
					null);
		}
	}

	private static ScavengeCheckpointJsonPersistence<TStreamId> ToDto(ScavengeCheckpoint checkpoint) {
		var dto = new ScavengeCheckpointJsonPersistence<TStreamId> {
			SchemaVersion = Version.V0,
			ScavengePoint = checkpoint.ScavengePoint,
		};

		switch (checkpoint) {
			case ScavengeCheckpoint.Accumulating x:
				dto.CheckpointStage = Stage.Accumulating;
				dto.DoneLogicalChunkNumber = x.DoneLogicalChunkNumber;
				break;

			case ScavengeCheckpoint.Calculating<TStreamId> x:
				dto.CheckpointStage = Stage.Calculating;
				dto.DoneStreamHandle = x.DoneStreamHandle;
				break;

			case ScavengeCheckpoint.ExecutingChunks x:
				dto.CheckpointStage = Stage.ExecutingChunks;
				dto.DoneLogicalChunkNumber = x.DoneLogicalChunkNumber;
				break;

			case ScavengeCheckpoint.MergingChunks _:
				dto.CheckpointStage = Stage.MergingChunks;
				break;

			case ScavengeCheckpoint.ExecutingIndex _:
				dto.CheckpointStage = Stage.ExecutingIndex;
				break;

			case ScavengeCheckpoint.Cleaning _:
				dto.CheckpointStage = Stage.Cleaning;
				break;

			case ScavengeCheckpoint.Done _:
				dto.CheckpointStage = Stage.Done;
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(checkpoint),
					checkpoint,
					null);
		}

		return dto;
	}

	public static bool TryDeserialize(string input, out ScavengeCheckpoint checkpoint) {
		try {
			var dto = JsonConvert.DeserializeObject<ScavengeCheckpointJsonPersistence<TStreamId>>(
				input,
				Json.JsonSettings);
			checkpoint = dto.ToDomain();
			return checkpoint != null;
		} catch {
			// no op
		}

		checkpoint = default;
		return false;
	}

	public static string Serialize(ScavengeCheckpoint checkpoint) {
		var dto = ToDto(checkpoint);
		return JsonConvert.SerializeObject(dto, Json.JsonSettings);
	}
}
