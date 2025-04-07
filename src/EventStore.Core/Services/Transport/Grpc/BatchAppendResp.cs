// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

// ReSharper disable once CheckNamespace

namespace EventStore.Client.Streams;

partial class BatchAppendResp {
	internal bool IsClosing { get; set; }
	partial class Types {
		partial class Success {
			public static Success Completed(long commitPosition, long preparePosition, long currentVersion) => new() {
				positionOptionCase_ = (commitPosition, preparePosition) switch {
					( >= 0, >= 0) => PositionOptionOneofCase.Position,
					_ => PositionOptionOneofCase.NoPosition
				},
				positionOption_ = (commitPosition, preparePosition) switch {
					( >= 0, >= 0) => new AllStreamPosition {
						CommitPosition = (ulong)commitPosition,
						PreparePosition = (ulong)preparePosition
					},
					_ => new Google.Protobuf.WellKnownTypes.Empty()
				},
				currentRevisionOptionCase_ = currentVersion switch {
					>= 0 => CurrentRevisionOptionOneofCase.CurrentRevision,
					_ => CurrentRevisionOptionOneofCase.NoStream
				},
				currentRevisionOption_ = currentVersion switch {
					>= 0 => (ulong)currentVersion,
					_ => new Google.Protobuf.WellKnownTypes.Empty()
				}
			};
		}
	}
}
