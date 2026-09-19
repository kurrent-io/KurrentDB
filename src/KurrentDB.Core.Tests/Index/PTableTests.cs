// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.IO;
using KurrentDB.Core.Index;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Index;

[TestFixture]
public class PTableTests {

	public static IEnumerable<TestCaseData> LayoutCases() {
		// V1 (16 bytes): Version Int32 @0, Stream UInt32 @4, Position Int64 @8
		yield return new TestCaseData(
			PTableVersions.IndexV1,
			new IndexEntry(stream: 0x11223344, version: 0x0A0B0C0D, position: 0x1122334455667788),
			new byte[] {
				0x0D, 0x0C, 0x0B, 0x0A,                         // Version (Int32)
				0x44, 0x33, 0x22, 0x11,                         // Stream (UInt32)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Position (Int64)
			}).SetName("V1");

		// V2 (20 bytes): Version Int32 @0, Stream UInt64 @4, Position Int64 @12
		yield return new TestCaseData(
			PTableVersions.IndexV2,
			new IndexEntry(stream: 0x1122334455667788, version: 0x0A0B0C0D, position: 0x0102030405060708),
			new byte[] {
				0x0D, 0x0C, 0x0B, 0x0A,                         // Version (Int32)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
				0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // Position (Int64)
			}).SetName("V2");

		// V3 (24 bytes): Version Int64 @0, Stream UInt64 @8, Position Int64 @16
		yield return new TestCaseData(
			PTableVersions.IndexV3,
			new IndexEntry(stream: 0x1122334455667788, version: 0x0A0B0C0D0E0F1011, position: 0x0102030405060708),
			new byte[] {
				0x11, 0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, // Version (Int64)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
				0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // Position (Int64)
			}).SetName("V3");

		// V4 shares the V3 layout (24 bytes).
		yield return new TestCaseData(
			PTableVersions.IndexV4,
			new IndexEntry(stream: 0x1122334455667788, version: 0x0A0B0C0D0E0F1011, position: 0x0102030405060708),
			new byte[] {
				0x11, 0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, // Version (Int64)
				0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, // Stream (UInt64)
				0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // Position (Int64)
			}).SetName("V4");
	}

	[TestCaseSource(nameof(LayoutCases))]
	public void ReadIndexEntryFrom_reads_the_versioned_binary_layout_from_stream(byte ptableVersion, IndexEntry expected, byte[] bytes) {
		using var stream = new MemoryStream(bytes);

		var entry = PTable.ReadIndexEntryFrom(stream, ptableVersion);

		Assert.AreEqual(expected.Stream, entry.Stream);
		Assert.AreEqual(expected.Version, entry.Version);
		Assert.AreEqual(expected.Position, entry.Position);
		Assert.AreEqual(bytes.Length, stream.Position, "should consume exactly the entry's bytes");
	}

	[TestCaseSource(nameof(LayoutCases))]
	public void AppendIndexEntryTo_writes_the_versioned_binary_layout(byte ptableVersion, IndexEntry entry, byte[] expected) {
		using var stream = new MemoryStream();

		PTable.AppendIndexEntryTo(stream, in entry, ptableVersion);

		CollectionAssert.AreEqual(expected, stream.ToArray());
	}
}
