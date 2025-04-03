// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using EventStore.Core.DataStructures.ProbabilisticFilter;
using EventStore.Core.Index.Hashes;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures;

public enum PersistenceStrategy {
	MemoryMapped,
	FileStream,
}

[TestFixture(PersistenceStrategy.MemoryMapped, PersistenceStrategy.MemoryMapped)]
[TestFixture(PersistenceStrategy.MemoryMapped, PersistenceStrategy.FileStream)]
[TestFixture(PersistenceStrategy.FileStream, PersistenceStrategy.MemoryMapped)]
[TestFixture(PersistenceStrategy.FileStream, PersistenceStrategy.FileStream)]
public class persistent_stream_bloom_filter : SpecificationWithDirectoryPerTestFixture {
	private readonly PersistenceStrategy _forCreate;
	private readonly PersistenceStrategy _forOpen;

	public persistent_stream_bloom_filter(PersistenceStrategy forCreate, PersistenceStrategy forOpen) {
		_forCreate = forCreate;
		_forOpen = forOpen;
	}

	PersistentStreamBloomFilter GenSut(string path, bool create, long size, ILongHasher<string> hasher) =>
		(create ? _forCreate : _forOpen) switch {
			PersistenceStrategy.MemoryMapped =>
				new PersistentStreamBloomFilter(
					new MemoryMappedFilePersistence(size, path, create),
					hasher: hasher),

			PersistenceStrategy.FileStream =>
				new PersistentStreamBloomFilter(
					new FileStreamPersistence(size, path, create),
					hasher: hasher),

			_ => throw new ArgumentOutOfRangeException(),
	};

	private static string GenerateCharset() {
		var charset = "";
		for (var c = 'a'; c <= 'z'; c++) {
			charset += c;
		}
		for (var c = 'A'; c <= 'Z'; c++) {
			charset += c;
		}
		for (var c = '0'; c <= '9'; c++) {
			charset += c;
		}
		charset += "!@#$%^&*()-_";
		return charset;
	}

	private static string GenerateRandomString(int length, string charset, Random random) {
		string s = "";
		for (var j = 0; j < length; j++) {
			s += charset[random.Next() % charset.Length];
		}

		return s;
	}

	private static string[] GenerateRandomStrings(int count, int maxLength) {
		var random = new Random(123);
		var strings = new List<string>();
		var charset = GenerateCharset();

		for (int i = 0; i < count; i++) {
			int length = 1 + random.Next() % maxLength;
			strings.Add(GenerateRandomString(length, charset, random));
		}

		return strings.ToArray();
	}

	[TestFixture(PersistenceStrategy.MemoryMapped, PersistenceStrategy.MemoryMapped)]
	[TestFixture(PersistenceStrategy.MemoryMapped, PersistenceStrategy.FileStream)]
	[TestFixture(PersistenceStrategy.FileStream, PersistenceStrategy.MemoryMapped)]
	[TestFixture(PersistenceStrategy.FileStream, PersistenceStrategy.FileStream)]
	private class with_fixed_size_filter : persistent_stream_bloom_filter {
		private PersistentStreamBloomFilter _filter;
		private string _path;

		public with_fixed_size_filter(PersistenceStrategy forCreate, PersistenceStrategy forOpen)
			: base(forCreate, forOpen) {
		}

		[SetUp]
		public void SetUp() {
			_path = GetTempFilePath();
			_filter = GenSut(_path, create: true, BloomFilterAccessor.MinSizeKB * 1000, hasher: null);
		}

		[TearDown]
		public void Teardown() => _filter?.Dispose();

		[Test]
		public void creates_the_file_on_disk() => Assert.That(File.Exists(_path));

		[Test]
		public void can_close_and_reopen() {
			_filter.Add("hello");
			_filter.Flush();
			_filter.Dispose();
			using var newFilter = GenSut(_path, create: false, BloomFilterAccessor.MinSizeKB * 1000, hasher: null);
			Assert.IsTrue(newFilter.MightContain("hello"));
		}

		[Test]
		public void can_detect_incorrect_size() {
			_filter.Add("hello");
			_filter.Flush();
			_filter.Dispose();

			Assert.Throws<SizeMismatchException>(() => {
				using var newFilter = GenSut(_path, create: false, BloomFilterAccessor.MinSizeKB * 1000 + 1, hasher: null);
			});
		}

		[Test]
		public void creates_correct_header() {
			_filter.Dispose();
			using var fileStream = File.Open(_path, FileMode.Open);
			var binaryReader = new BinaryReader(fileStream);

			var version = binaryReader.ReadByte();
			binaryReader.ReadByte();
			binaryReader.ReadByte();
			binaryReader.ReadByte();
			var corruptionRebuildCount = binaryReader.ReadInt32();
			var numBits = binaryReader.ReadInt64();
			Assert.AreEqual( 0x01, version);
			Assert.AreEqual( 0, corruptionRebuildCount);
			Assert.AreEqual(BloomFilterAccessor.MinSizeKB * 1000 * 8, numBits);
		}

		[Test]
		public void supports_adding_long_strings() {
			var charset = GenerateCharset();
			var random = new Random(123);
			var longString = GenerateRandomString(10000, charset, random);

			Assert.IsFalse(_filter.MightContain(longString));
			_filter.Add(longString);
			Assert.IsTrue(_filter.MightContain(longString));
		}
	}

	[Test, Combinatorial]
	public void has_false_positives_with_probability_p(
		[Values(BloomFilterAccessor.MinSizeKB*1000,2* BloomFilterAccessor.MinSizeKB*1000)] long size,
		[Values(0.001,0.02,0.05,0.1,0.2)] double p
	) {
		using var filter = GenSut(GetTempFilePath(), create: true, size, hasher: null);
		var n = (int) filter.CalculateOptimalNumItems(p);

		var random = new Random(123);
		var charset = GenerateCharset();

		var list = new List<string>();

		var selected = new HashSet<string>();
		//generate 2n distinct items
		for (int i = 0; i < 2 * n; i++) {
			while (true) {
				var length = 1 + random.Next() % 10;
				var s = GenerateRandomString(length, charset, random);
				if (selected.Contains(s)) continue;
				list.Add(s);
				selected.Add(s);
				break;
			}
		}

		//add first n distinct items to the filter
		for (int i = 0; i < n; i++) {
			filter.Add(list[i]);
		}

		//expected number of false positives
		var expectedFalsePositives = Convert.ToInt32(Math.Ceiling(n * p));

		//the second n distinct items should not exist but there may be some false positives
		var falsePositives = 0;
		for (var i = n ; i < 2*n; i ++) {
			if (filter.MightContain(list[i])) {
				falsePositives++;
			}
		}

		//X = random variable that takes value 1 with probability p and value 0 with probability (1-p)
		//var(X) = E(X^2) - E(X)^2 = p - p*p;
		//var(X1 + X2 + X3 + ... + Xn) = n*var(Xi); //variance of n uncorrelated random variables
		//var(X1 + X2 + X3 + ... + Xn) = n*(p-p*p);
		var variance = n * (p - (p * p));
		var standardDeviation = Math.Sqrt(variance);
		var threeStandardDeviations = 3 * standardDeviation; //99.7%

		if (falsePositives > 0)
			Console.Out.WriteLine("n: {0:N0}, p:{1:N3}. Found {2:N0} false positives. Expected false positives: {3:N0}. Standard deviation: {4:N2}",
				n, p, falsePositives, expectedFalsePositives, standardDeviation);

		Assert.LessOrEqual(falsePositives, expectedFalsePositives + threeStandardDeviations);
		Assert.GreaterOrEqual(falsePositives, Math.Max(0, expectedFalsePositives - threeStandardDeviations));
	}

	[Test, Category("LongRunning")]
	public void always_returns_true_when_an_item_was_added([Range(10_000, 100_000, 13337)] long size) {
		using var filter = GenSut(GetTempFilePath(), create: true, size, hasher: null);
		var strings = GenerateRandomStrings((int)filter.CalculateOptimalNumItems(PersistentBloomFilter.RecommendedFalsePositiveProbability), 100);

		//no items added yet
		foreach (var s in strings) {
			Assert.IsFalse(filter.MightContain(s));
		}

		//add the items and verify their existence
		foreach (var s in strings) {
			filter.Add(s);
			Assert.IsTrue(filter.MightContain(s));
		}

		//all the items should exist
		foreach (var s in strings) {
			Assert.IsTrue(filter.MightContain(s));
		}
	}

	[Test]
	public void throws_argument_out_of_range_exception_when_given_negative_size() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			GenSut(GetTempFilePath(), create: true, size: -1, hasher: null));
	}

	[Test]
	public void throws_argument_out_of_range_exception_when_given_zero_size() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			GenSut(GetTempFilePath(), create: true, size: 0, hasher: null));
	}

	[Test]
	public void throws_argument_out_of_range_exception_when_size_less_than_min_size() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			GenSut(GetTempFilePath(), create: true, size: BloomFilterAccessor.MinSizeKB * 1000 - 1, hasher: null));
	}

	[Test]
	public void throws_argument_out_of_range_exception_when_size_greater_than_max_size() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			GenSut(GetTempFilePath(), create: true, size: BloomFilterAccessor.MaxSizeKB * 1000 + 1, hasher: null));
	}
}
