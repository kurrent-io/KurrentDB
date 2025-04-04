// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace EventStore.LogV3;

// View of a stream write record
// interprets the payload
public struct StreamWriteRecord : IRecordView {
	private readonly ReadOnlyMemory<byte> _event;

	public ReadOnlyMemory<byte> Bytes => Record.Bytes;
	public RecordView<Raw.StreamWriteHeader> Record { get; }
	public ref readonly Raw.RecordHeader Header => ref Record.Header;
	public ref readonly Raw.StreamWriteId WriteId => ref Record.RecordId<Raw.StreamWriteId>();
	public ref readonly Raw.StreamWriteHeader SubHeader => ref Record.SubHeader;
	public ReadOnlyMemory<byte> Payload => Record.Payload;
	public StreamWriteSystemMetadata SystemMetadata { get; }

	public StreamWriteRecord(RecordView<Raw.StreamWriteHeader> record) {
		Record = record;

		var slicer = Record.Payload.Slicer();
		var systemMetadata = slicer.Slice(Record.SubHeader.MetadataSize);
		_event = slicer.Remaining;

		SystemMetadata = StreamWriteSystemMetadata.Parser.ParseFrom(new ReadOnlySequence<byte>(systemMetadata));
	}

	public EventRecord Event {
		get {
			var slicer = _event.Slicer();
			ref readonly var eventHeader = ref MemoryMarshal.AsRef<Raw.EventHeader>(slicer.Remaining.Span);
			var eventBytes = slicer.Slice(eventHeader.EventSize);
			return new EventRecord(eventBytes);
		}
	}
}
