// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index.Hashes;

namespace KurrentDB.Kontext.Tests.Indexing;

public class StreamHasherTests {
	/// <summary>
	/// StreamHasher must produce the same 64-bit id as KurrentDB's CompositeHasher so that
	/// vector-hit ids resolve via IReadIndex.ReadEventInfoForward_NoCollisions.
	/// </summary>
	[Test]
	[Arguments("alice-hobbies")]
	[Arguments("$kontext-memory:default")]
	[Arguments("order-123")]
	[Arguments("")]
	public async Task Matches_Kurrent_CompositeHasher(string streamName) {
		var kurrent = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		await Assert.That(StreamHasher.Hash(streamName)).IsEqualTo(kurrent.Hash(streamName));
	}
}