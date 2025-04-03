// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;

namespace EventStore.LogV3;

// Mutable view of a record
public struct MutableRecordView<TSubHeader> where TSubHeader : unmanaged {
	private readonly SlicedRecord _sliced;

	public Memory<byte> Bytes => _sliced.Bytes;
	public ref Raw.RecordHeader Header => ref MemoryMarshal.AsRef<Raw.RecordHeader>(_sliced.HeaderMemory.Span);
	public ref T RecordId<T>() where T : unmanaged =>
		ref MemoryMarshal.AsRef<T>(_sliced.HeaderMemory[Raw.RecordHeader.RecordIdOffset..].Span);
	public ref TSubHeader SubHeader => ref MemoryMarshal.AsRef<TSubHeader>(_sliced.SubHeaderMemory.Span);
	public Memory<byte> Payload => _sliced.PayloadMemory;

	public static implicit operator RecordView<TSubHeader>(MutableRecordView<TSubHeader> record) =>
		new RecordView<TSubHeader>(record.Bytes);

	public static MutableRecordView<TSubHeader> Create(int payloadLength) {
		// todo: later consider pool, inject allocator/use a factory
		// ideally we could grab space _in the chunk_ that we are about to write to
		var length = CalculateLength(payloadLength);
		var bytes = new byte[length].AsMemory()[..length];
		return new MutableRecordView<TSubHeader>(bytes);
	}

	unsafe static int CalculateLength(int payloadSize) =>
		Raw.RecordHeader.Size + sizeof(TSubHeader) + payloadSize;

	public MutableRecordView(Memory<byte> bytes) {
		_sliced = SlicedRecordCreator<TSubHeader>.Create(bytes);
	}
}
