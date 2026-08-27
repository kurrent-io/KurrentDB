// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using KurrentDB.Core.Index;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Index;

[TestFixture]
public class PTableMidpointTests {
	// Midpoint has a single layout (24 bytes): Version Int64 @0, Stream UInt64 @8, ItemIndex Int64 @16

	private static readonly PTable.Midpoint _midpoint = new(
		stream: 0x1122334455667788,
		version: 0x0A0B0C0D0E0F1011,
		itemIndex: 0x0102030405060708);

	private static readonly byte[] _bytes = [
		0x11, 0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, // Version (Int64)
		0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
		0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // ItemIndex (Int64)
	];

	[Test]
	public void Size_matches_the_binary_layout() {
		Assert.AreEqual(_bytes.Length, PTable.Midpoint.Size);
	}

	[Test]
	public void ReadFrom_reads_the_binary_layout_from_stream() {
		using var stream = new MemoryStream(_bytes);

		var midpoint = PTable.Midpoint.ReadFrom(stream);

		Assert.AreEqual(_midpoint.Stream, midpoint.Stream);
		Assert.AreEqual(_midpoint.Version, midpoint.Version);
		Assert.AreEqual(_midpoint.ItemIndex, midpoint.ItemIndex);
		Assert.AreEqual(_bytes.Length, stream.Position, "should consume exactly the midpoint's bytes");
	}

	[Test]
	public void ReadFrom_throws_when_the_stream_has_fewer_bytes_than_the_midpoint() {
		using var stream = new MemoryStream(_bytes[..^1]);

		Assert.Throws<EndOfStreamException>(() => PTable.Midpoint.ReadFrom(stream));
	}

	[Test]
	public void AppendTo_writes_the_binary_layout() {
		using var stream = new MemoryStream();

		_midpoint.AppendTo(stream);

		CollectionAssert.AreEqual(_bytes, stream.ToArray());
	}

	[Test]
	public void AppendTo_appends_to_the_current_stream_position() {
		using var stream = new MemoryStream();
		stream.WriteByte(0xAB);

		_midpoint.AppendTo(stream);

		CollectionAssert.AreEqual((byte[])[0xAB, .. _bytes], stream.ToArray());
	}

	[Test]
	public void AppendTo_then_ReadFrom_round_trips_the_midpoint() {
		using var stream = new MemoryStream();
		_midpoint.AppendTo(stream);
		stream.Position = 0;

		var roundTripped = PTable.Midpoint.ReadFrom(stream);

		Assert.AreEqual(_midpoint.Stream, roundTripped.Stream);
		Assert.AreEqual(_midpoint.Version, roundTripped.Version);
		Assert.AreEqual(_midpoint.ItemIndex, roundTripped.ItemIndex);
	}

	[Test]
	public void Key_exposes_the_stream_and_version() {
		Assert.AreEqual(_midpoint.Stream, _midpoint.Key.Stream);
		Assert.AreEqual(_midpoint.Version, _midpoint.Key.Version);
	}

	[Test]
	public void Midpoint_constructed_from_a_key_writes_the_same_binary_layout() {
		var fromKey = new PTable.Midpoint(_midpoint.Key, _midpoint.ItemIndex);
		using var stream = new MemoryStream();

		fromKey.AppendTo(stream);

		CollectionAssert.AreEqual(_bytes, stream.ToArray());
	}
}
