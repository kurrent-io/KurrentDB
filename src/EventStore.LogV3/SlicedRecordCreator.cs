// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.LogV3;

// This class slices bytes up according to the headers in the desired record
// It exists to keep the logic between the readonly/normal versions in the same place.
public static class SlicedRecordCreator<TSubHeader> where TSubHeader : unmanaged {
	public static SlicedRecord Create(Memory<byte> bytes) {
		var slicer = bytes.Slicer();
		return new SlicedRecord {
			Bytes = slicer.Remaining,
			HeaderMemory = slicer.Slice(Raw.RecordHeader.Size),
			SubHeaderMemory = slicer.Slice<TSubHeader>(),
			PayloadMemory = slicer.Remaining,
		};
	}

	public static ReadOnlySlicedRecord Create(ReadOnlyMemory<byte> bytes) {
		var slicer = bytes.Slicer();
		return new ReadOnlySlicedRecord {
			Bytes = slicer.Remaining,
			HeaderMemory = slicer.Slice(Raw.RecordHeader.Size),
			SubHeaderMemory = slicer.Slice<TSubHeader>(),
			PayloadMemory = slicer.Remaining,
		};
	}
}
