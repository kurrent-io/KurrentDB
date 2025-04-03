// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Validation;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.Transforms.Identity;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

[TestFixture]
public class when_truncating_into_the_middle_of_completed_chunk : SpecificationWithDirectoryPerTestFixture {
	private TFChunkDbConfig _config;
	private byte[] _file1Contents;
	private byte[] _file2Contents;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_config = TFChunkHelper.CreateDbConfigEx(PathName, 3711, 5500, 5500, -1, 1111, 1000, -1);

		var rnd = new Random();
		_file1Contents = new byte[_config.ChunkSize];
		_file2Contents = new byte[_config.ChunkSize];
		rnd.NextBytes(_file1Contents);
		rnd.NextBytes(_file2Contents);

		DbUtil.CreateSingleChunk(_config, 0, GetFilePathFor("chunk-000000.000001"), contents: _file1Contents);
		DbUtil.CreateSingleChunk(_config, 1, GetFilePathFor("chunk-000001.000002"), contents: _file2Contents);
		DbUtil.CreateSingleChunk(_config, 2, GetFilePathFor("chunk-000002.000000"));
		DbUtil.CreateSingleChunk(_config, 3, GetFilePathFor("chunk-000003.000000"));

		var truncator = new TFChunkDbTruncator(_config, new ChunkLocalFileSystem(_config.Path), static _ => new IdentityChunkTransformFactory());
		await truncator.TruncateDb(_config.TruncateCheckpoint.ReadNonFlushed(), CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await using (var db = new TFChunkDb(_config)) {
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}

		Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000001")));
		Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000002")));
		Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);

		await base.TestFixtureTearDown();
	}

	[Test]
	public void writer_checkpoint_should_be_set_to_exactly_truncation_checkpoint() {
		Assert.AreEqual(1111, _config.WriterCheckpoint.Read());
		Assert.AreEqual(1111, _config.WriterCheckpoint.ReadNonFlushed());
	}

	[Test]
	public void chaser_checkpoint_should_be_adjusted_if_less_than_actual_truncate_checkpoint() {
		Assert.AreEqual(1111, _config.ChaserCheckpoint.Read());
		Assert.AreEqual(1111, _config.ChaserCheckpoint.ReadNonFlushed());
	}

	[Test]
	public void epoch_checkpoint_should_be_reset_if_less_than_actual_truncate_checkpoint() {
		Assert.AreEqual(-1, _config.EpochCheckpoint.Read());
		Assert.AreEqual(-1, _config.EpochCheckpoint.ReadNonFlushed());
	}

	[Test]
	public void truncate_checkpoint_should_be_reset_after_truncation() {
		Assert.AreEqual(-1, _config.TruncateCheckpoint.Read());
		Assert.AreEqual(-1, _config.TruncateCheckpoint.ReadNonFlushed());
	}

	[Test]
	public void chunks_after_truncation_point_are_deleted() {
		Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000001")));
		Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000002")));
		Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
	}

	[Test]
	public void contents_of_first_chunk_should_be_untouched() {
		var contents = new byte[_config.ChunkSize];
		using (var fs = File.OpenRead(GetFilePathFor("chunk-000000.000001"))) {
			fs.Position = ChunkHeader.Size;
			fs.Read(contents, 0, contents.Length);
			Assert.AreEqual(_file1Contents, contents);
		}
	}

	[Test]
	public void ongoing_chunk_should_have_full_size_and_filled_with_zeros_after_writer_checkpoint() {
		var fileInfo = new FileInfo(GetFilePathFor("chunk-000001.000002"));
		Assert.AreEqual(TFChunk.GetAlignedSize(ChunkHeader.Size + 1000 + ChunkFooter.Size), fileInfo.Length);

		using (var fs = File.OpenRead(fileInfo.FullName)) {
			var leftDataSize = (int)(_config.WriterCheckpoint.Read() % _config.ChunkSize);
			var leftData = new byte[leftDataSize];
			var shouldBeZeros = new byte[_config.ChunkSize - leftDataSize + ChunkFooter.Size];

			fs.Position = ChunkHeader.Size;
			fs.Read(leftData, 0, leftData.Length);

			var shouldBeLeft = new byte[leftDataSize];
			Buffer.BlockCopy(_file2Contents, 0, shouldBeLeft, 0, leftDataSize);
			Assert.AreEqual(shouldBeLeft, leftData);

			fs.Position = ChunkHeader.Size + _config.WriterCheckpoint.Read() % _config.ChunkSize;
			fs.Read(shouldBeZeros, 0, shouldBeZeros.Length);

			Assert.IsTrue(shouldBeZeros.All(x => x == 0), "Chunk is not zeroed.");
		}
	}
}
