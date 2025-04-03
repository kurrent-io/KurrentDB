// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;

namespace EventStore.LogV3;

// Several records have a payload which is a dynamically sized string
public struct StringPayloadRecord {
	public static StringPayloadRecord<TSubHeader> Create<TSubHeader>(RecordView<TSubHeader> record)
		where TSubHeader : unmanaged =>
		new(record);

	public static StringPayloadRecord<TSubHeader> Create<TSubHeader>(MutableRecordView<TSubHeader> record)
		where TSubHeader : unmanaged =>
		new(record);
}

public struct StringPayloadRecord<TSubHeader> : IRecordView where TSubHeader : unmanaged {
	public ReadOnlyMemory<byte> Bytes => Record.Bytes;
	public RecordView<TSubHeader> Record { get; }

	public ref readonly Raw.RecordHeader Header => ref Record.Header;
	public ref readonly TSubHeader SubHeader => ref Record.SubHeader;
	public ReadOnlyMemory<byte> Payload => Record.Payload;
	public string StringPayload => Encoding.UTF8.GetString(Record.Payload.Span);

	public StringPayloadRecord(RecordView<TSubHeader> record) {
		Record = record;
	}
}
