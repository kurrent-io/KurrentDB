// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Validation;

[TestFixture]
public class when_validating_tfchunk_db_with_multi_chunks : SpecificationWithDirectory {
	[Test]
	public async Task with_not_enough_files_to_reach_checksum_throws() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 25000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 1, GetFilePathFor("chunk-000000.000000"));
			Assert.That(() => db.Open(verifyHash: false),
				Throws.Exception.InstanceOf<CorruptDatabaseException>()
					.With.InnerException.InstanceOf<ChunkNotFoundException>());
		}
	}

	[Test]
	public async Task with_checksum_inside_multi_chunk_throws() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 25000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 2, GetFilePathFor("chunk-000000.000000"));
			Assert.That(() => db.Open(verifyHash: false),
				Throws.Exception.InstanceOf<CorruptDatabaseException>()
					.With.InnerException.InstanceOf<ChunkNotFoundException>());
		}
	}

	[Test]
	public async Task allows_with_exactly_enough_file_to_reach_checksum_while_last_is_multi_chunk() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 30000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 2, GetFilePathFor("chunk-000001.000001"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000003.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task allows_next_new_chunk_when_checksum_is_exactly_in_between_two_chunks_if_last_is_ongoing_chunk() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 20000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 1, GetFilePathFor("chunk-000000.000001"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test, Ignore("Due to truncation such situation can happen, so must be considered valid.")]
	public async Task
		does_not_allow_next_new_chunk_when_checksum_is_exactly_in_between_two_chunks_and_last_is_multi_chunk() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 2, GetFilePathFor("chunk-000001.000000"));

			Assert.That(() => db.Open(verifyHash: false),
				Throws.Exception.InstanceOf<CorruptDatabaseException>()
					.With.InnerException.InstanceOf<BadChunkInDatabaseException>());
		}
	}

	[Test]
	public async Task old_version_of_chunks_are_removed() {
		File.Create(GetFilePathFor("foo")).Close();
		File.Create(GetFilePathFor("bla")).Close();

		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 450, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000002"));
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000005"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			DbUtil.CreateMultiChunk(config, 1, 3, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			DbUtil.CreateSingleChunk(config, 3, GetFilePathFor("chunk-000003.000000"));
			DbUtil.CreateSingleChunk(config, 3, GetFilePathFor("chunk-000003.000001"));
			DbUtil.CreateSingleChunk(config, 4, GetFilePathFor("chunk-000004.000007"));
			DbUtil.CreateOngoingChunk(config, 4, GetFilePathFor("chunk-000004.000008"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("foo")));
			Assert.IsTrue(File.Exists(GetFilePathFor("bla")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000005")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000004.000008")));
			Assert.AreEqual(5, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task
		when_checkpoint_is_exactly_on_the_boundary_of_chunk_the_last_chunk_could_be_not_present_but_should_be_created() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 1, GetFilePathFor("chunk-000000.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_checkpoint_is_exactly_on_the_boundary_of_chunk_the_last_chunk_could_be_present() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 1, GetFilePathFor("chunk-000000.000002"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000002")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000001")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_checkpoint_is_on_boundary_of_new_chunk_and_last_chunk_is_truncated_no_exception_is_thrown() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 300, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 2, GetFilePathFor("chunk-000001.000001"), physicalSize: 50,
				logicalSize: 150);

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000003.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task does_not_allow_checkpoint_to_point_into_the_middle_of_multichunk_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 1500, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 10, GetFilePathFor("chunk-000001.000001"));

			Assert.That(() => db.Open(verifyHash: false),
				Throws.Exception.InstanceOf<CorruptDatabaseException>()
					.With.InnerException.InstanceOf<BadChunkInDatabaseException>());
		}
	}

	[Test]
	public async Task allows_last_chunk_to_be_multichunk_when_checkpoint_point_at_the_start_of_next_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 4000, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 3, GetFilePathFor("chunk-000001.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000004.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}
}
