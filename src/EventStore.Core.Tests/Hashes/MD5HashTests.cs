// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Core.Hashing;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Core.Tests.Hashes;

[TestFixture]
public class MD5HashTests {
	[Test]
	public void does_not_include_previous_data_in_stream() {
		var bytes = new byte[1024];
		for (int i = 15; i < 1024; i++) {
			bytes[i] = (byte)(i % 255);
		}

		var stream = new MemoryStream(bytes);
		stream.Seek(16, SeekOrigin.Begin);
		var hash = MD5Hash.GetHashFor(stream);
		Array.Copy(hash, 0, bytes, 0, hash.Length);
		stream.Seek(16, SeekOrigin.Begin);
		var hash2 = MD5Hash.GetHashFor(stream);
		Assert.AreEqual(16, hash.Length);
		Assert.AreEqual(hash, hash2);
	}

	[Test]
	public void changing_data_in_stream_results_in_different_hash() {
		var bytes = new byte[1024];
		for (int i = 15; i < 1024; i++) {
			bytes[i] = (byte)(i % 255);
		}

		var stream = new MemoryStream(bytes);
		stream.Seek(16, SeekOrigin.Begin);
		var hash = MD5Hash.GetHashFor(stream);
		bytes[243] = 17;
		stream.Seek(16, SeekOrigin.Begin);
		var hash2 = MD5Hash.GetHashFor(stream);
		Assert.AreNotEqual(hash, hash2);
	}

	[Test]
	public void includes_correct_substream_data() {
		var bytes = new byte[1024];
		for (int i = 15; i < 1024; i++) {
			bytes[i] = (byte)(i % 255);
		}

		var stream = new MemoryStream(bytes);
		var hash = MD5Hash.GetHashFor(stream, 16, bytes.Length - 32);

		using (var md5 = MD5.Create()) {
			var referenceHash = md5.ComputeHash(bytes, 16, bytes.Length - 32);
			Assert.AreEqual(16, hash.Length);
			Assert.AreEqual(referenceHash, hash);
		}
	}

	[Test, Category("LongRunning"), Explicit]
	public void randomized_hash_verification_test() {
		var buf = new byte[1024];
		var seed = Environment.TickCount;
		Console.WriteLine("Seed: {0}", seed);
		new Random().NextBytes(buf);

		var stream = new MemoryStream(buf);

		using (var md5 = MD5.Create()) {
			for (int i = 0; i < buf.Length; ++i) {
				for (int j = i; j < buf.Length + 10; ++j) {
					var referenceHash = md5.ComputeHash(buf, i, Math.Min(buf.Length - i, j - i + 1));
					var hash = MD5Hash.GetHashFor(stream, i, j - i + 1);
					Assert.AreEqual(16, hash.Length);
					Assert.AreEqual(referenceHash, hash);
				}
			}
		}
	}
}
