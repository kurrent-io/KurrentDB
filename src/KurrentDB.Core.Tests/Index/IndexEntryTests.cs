// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using KurrentDB.Core.Index;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Index;

[TestFixture(typeof(IndexEntry.V1))]
[TestFixture(typeof(IndexEntry.V2))]
[TestFixture(typeof(IndexEntry.V3))]
public class IndexEntryTests<T> where T : struct, IndexEntry.ILayout<T> {
	private readonly IndexEntry _entry;
	private readonly byte[] _bytes;

	public IndexEntryTests() {
		if (typeof(T) == typeof(IndexEntry.V1)) {
			// V1 (16 bytes): Version Int32 @0, Stream UInt32 @4, Position Int64 @8
			_entry = new IndexEntry(stream: 0x11223344, version: 0x0A0B0C0D, position: 0x1122334455667788);
			_bytes = [
				0x0D, 0x0C, 0x0B, 0x0A,                         // Version (Int32)
				0x44, 0x33, 0x22, 0x11,                         // Stream (UInt32)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Position (Int64)
			];
		} else if (typeof(T) == typeof(IndexEntry.V2)) {
			// V2 (20 bytes): Version Int32 @0, Stream UInt64 @4, Position Int64 @12
			_entry = new IndexEntry(stream: 0x1122334455667788, version: 0x0A0B0C0D, position: 0x0102030405060708);
			_bytes = [
				0x0D, 0x0C, 0x0B, 0x0A,                         // Version (Int32)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
				0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // Position (Int64)
			];
		} else if (typeof(T) == typeof(IndexEntry.V3)) {
			// V3 (24 bytes): Version Int64 @0, Stream UInt64 @8, Position Int64 @16
			_entry = new IndexEntry(stream: 0x1122334455667788, version: 0x0A0B0C0D0E0F1011, position: 0x0102030405060708);
			_bytes = [
				0x11, 0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, // Version (Int64)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
				0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // Position (Int64)
			];
		} else {
			throw new NotSupportedException($"No binary layout defined for {typeof(T).Name}.");
		}
	}

	[Test]
	public void Size_matches_the_versioned_binary_layout() {
		Assert.AreEqual(_bytes.Length, T.Size);
	}

	// The version struct's own static members, exercised without going through IndexEntry's wrappers.

	[Test]
	public void Version_ReadFrom_reads_the_versioned_binary_layout_from_span() {
		var entry = T.ReadFrom(_bytes);

		Assert.AreEqual(_entry.Stream, entry.Stream);
		Assert.AreEqual(_entry.Version, entry.Version);
		Assert.AreEqual(_entry.Position, entry.Position);
	}

	[Test]
	public void Version_ReadFrom_ignores_bytes_beyond_the_entry() {
		byte[] buffer = [.. _bytes, 0xFF, 0xFF, 0xFF, 0xFF];

		var entry = T.ReadFrom(buffer);

		Assert.AreEqual(_entry.Stream, entry.Stream);
		Assert.AreEqual(_entry.Version, entry.Version);
		Assert.AreEqual(_entry.Position, entry.Position);
	}

	[Test]
	public void Version_ReadFrom_throws_when_the_span_is_shorter_than_the_entry() {
		var buffer = _bytes[..^1];

		Assert.Throws<ArgumentOutOfRangeException>(() => T.ReadFrom(buffer));
	}

	[Test]
	public void Version_AppendTo_writes_the_versioned_binary_layout_to_stream() {
		using var stream = new MemoryStream();

		T.AppendTo(stream, in _entry);

		CollectionAssert.AreEqual(_bytes, stream.ToArray());
	}

	[Test]
	public void Version_AppendTo_appends_to_the_current_stream_position() {
		using var stream = new MemoryStream();
		stream.WriteByte(0xAB);

		T.AppendTo(stream, in _entry);

		CollectionAssert.AreEqual((byte[])[0xAB, .. _bytes], stream.ToArray());
	}

	// IndexEntry's generic wrappers over the version struct.

	[Test]
	public void ReadFrom_reads_the_versioned_binary_layout_from_stream() {
		using var stream = new MemoryStream(_bytes);

		var entry = IndexEntry.ReadFrom<T>(stream);

		Assert.AreEqual(_entry.Stream, entry.Stream);
		Assert.AreEqual(_entry.Version, entry.Version);
		Assert.AreEqual(_entry.Position, entry.Position);
		Assert.AreEqual(_bytes.Length, stream.Position, "should consume exactly the entry's bytes");
	}

	[Test]
	public void AppendTo_writes_the_versioned_binary_layout() {
		using var stream = new MemoryStream();

		_entry.AppendTo<T>(stream);

		CollectionAssert.AreEqual(_bytes, stream.ToArray());
	}

	[Test]
	public void AppendTo_then_ReadFrom_round_trips_the_entry() {
		using var stream = new MemoryStream();
		_entry.AppendTo<T>(stream);
		stream.Position = 0;

		var roundTripped = IndexEntry.ReadFrom<T>(stream);

		Assert.AreEqual(_entry, roundTripped);
	}

	[Test]
	public void ReadFrom_throws_when_the_stream_has_fewer_bytes_than_the_entry() {
		using var stream = new MemoryStream(_bytes[..^1]);

		Assert.Throws<EndOfStreamException>(() => IndexEntry.ReadFrom<T>(stream));
	}
}
