// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index;

[TestFixture]
public class IndexEntryTests {
	[Test]
	public void key_is_made_of_stream_and_version() {
		var entry = new IndexEntryV1 {Stream = 0x01, Version = 0x12};
		Assert.AreEqual(0x0000000100000012, entry.Key);
	}

	[Test]
	public void bytes_is_made_of_key_and_position() {
		unsafe {
			var entry = new IndexEntryV1 {Stream = 0x0101, Version = 0x1234, Position = 0xFFFF};
			Assert.AreEqual(0x34, entry.Bytes[0]);
			Assert.AreEqual(0x12, entry.Bytes[1]);
			Assert.AreEqual(0x00, entry.Bytes[2]);
			Assert.AreEqual(0x00, entry.Bytes[3]);
			Assert.AreEqual(0x01, entry.Bytes[4]);
			Assert.AreEqual(0x01, entry.Bytes[5]);
			Assert.AreEqual(0x00, entry.Bytes[6]);
			Assert.AreEqual(0x00, entry.Bytes[7]);
			Assert.AreEqual(0xFF, entry.Bytes[8]);
			Assert.AreEqual(0xFF, entry.Bytes[9]);
			Assert.AreEqual(0x00, entry.Bytes[10]);
			Assert.AreEqual(0x00, entry.Bytes[11]);
			Assert.AreEqual(0x00, entry.Bytes[12]);
			Assert.AreEqual(0x00, entry.Bytes[13]);
			Assert.AreEqual(0x00, entry.Bytes[14]);
			Assert.AreEqual(0x00, entry.Bytes[15]);
		}
	}
}
