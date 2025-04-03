// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Hashes;

[TestFixture]
public class xxhash_hash_should {
	// calculated from reference XXhash implementation at http://code.google.com/p/xxhash/
	public static uint XXHashReferenceVerificationValue = 0x56D249B1;

	[Test]
	public void pass_smhasher_verification_test() {
		Assert.IsTrue(SMHasher.VerificationTest(new XXHashUnsafe(), XXHashReferenceVerificationValue));
	}

	[Test, Category("LongRunning"), Explicit]
	public void pass_smhasher_sanity_test() {
		Assert.IsTrue(SMHasher.SanityTest(new XXHashUnsafe()));
	}
}
