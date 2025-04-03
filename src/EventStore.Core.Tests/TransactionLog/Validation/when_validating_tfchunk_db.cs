// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace EventStore.Core.Tests.TransactionLog.Validation;

[TestFixture]
public class when_validating_tfchunk_db : SpecificationWithDirectory {
	[Test]
	public async Task detect_no_database() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 4000, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ChunkNotFoundException);
		}
	}

	[Test]
	public async Task with_file_of_wrong_size_database_corruption_is_detected() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 500);
		await using (var db = new TFChunkDb(config)) {
			File.WriteAllText(GetFilePathFor("chunk-000000.000000"), "this is just some test blahbydy blah");
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task with_not_enough_files_to_reach_checksum_throws() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 15000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ChunkNotFoundException);
		}
	}

	[Test]
	public async Task allows_with_exactly_enough_file_to_reach_checksum() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}
	}

	[Test]
	public async Task does_not_allow_not_completed_not_last_chunks() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 4000, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			DbUtil.CreateOngoingChunk(config, 3, GetFilePathFor("chunk-000003.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task allows_next_new_chunk_when_checksum_is_exactly_in_between_two_chunks() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}
	}

	[Test, Ignore("Due to truncation such situation can happen, so must be considered valid.")]
	public async Task does_not_allow_next_new_completed_chunk_when_checksum_is_exactly_in_between_two_chunks() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task
		allows_last_chunk_to_be_not_completed_when_checksum_is_exactly_in_between_two_chunks_and_no_next_chunk_exists() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}
	}

	[Test]
	public async Task
		does_not_allow_pre_last_chunk_to_be_not_completed_when_checksum_is_exactly_in_between_two_chunks_and_next_chunk_exists() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 10000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test, Ignore("Not valid test now after disabling size validation on ongoing TFChunk ")]
	public async Task with_wrong_size_file_less_than_checksum_throws() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 15000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000000"),
				actualDataSize: config.ChunkSize - 1000);
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task one_sequential_extraneous_file_does_not_throw_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 9000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}
	}

	[Test]
	public async Task one_sequential_extraneous_file_with_non_zero_version_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 9000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ExtraneousFileFoundException);
		}
	}

	[Test]
	public async Task one_non_sequential_extraneous_file_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 9000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ExtraneousFileFoundException);
		}
	}

	[Test]
	public async Task when_in_multiple_extraneous_latest_version_of_files_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 15000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000")); // extraneous file, but valid case
			DbUtil.CreateSingleChunk(config, 3, GetFilePathFor("chunk-000003.000000")); // extraneous file
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ExtraneousFileFoundException);
			Assert.True(ex.InnerException.Message.Contains("chunk-000003.000000"));
		}
	}

	[Test]
	public async Task when_in_multiple_extraneous_old_version_of_files_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 15000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000001"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ExtraneousFileFoundException);
			Assert.True(ex.InnerException.Message.Contains("chunk-000002.000000"));
		}
	}

	[Test]
	public async Task when_in_multiple_missing_file_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 25000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ChunkNotFoundException);
		}
	}

	[Test]
	public async Task when_in_brand_new_extraneous_files_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 4, GetFilePathFor("chunk-000004.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ExtraneousFileFoundException);
		}
	}

	[Test]
	public async Task when_a_chaser_checksum_is_ahead_of_writer_checksum_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfigEx(PathName, 0, 11,-1,-1,-1,1000,-1);
		await using (var db = new TFChunkDb(config)) {
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ReaderCheckpointHigherThanWriterException);
		}
	}

	[Test]
	public async Task when_an_epoch_checksum_is_ahead_of_writer_checksum_throws_corrupt_database_exception() {
		var config = TFChunkHelper.CreateDbConfigEx(PathName, 0, 0, 11,-1,-1,1000,-1);
		await using (var db = new TFChunkDb(config)) {
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is ReaderCheckpointHigherThanWriterException);
		}
	}

	[Test]
	public async Task allows_no_files_when_checkpoint_is_zero() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		await using (var db = new TFChunkDb(config)) {
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
		}
	}

	[Test]
	public async Task allows_first_correct_ongoing_chunk_when_checkpoint_is_zero() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateOngoingChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
		}
	}

	[Test, Ignore("Due to truncation such situation can happen, so must be considered valid.")]
	public async Task does_not_allow_first_completed_chunk_when_checkpoint_is_zero() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task allows_checkpoint_to_point_into_the_middle_of_completed_chunk_when_enough_actual_data_in_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 1500, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"), actualDataSize: 500);

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test, Ignore("We do not check this as it is too erroneous to read ChunkFooter from ongoing chunk...")]
	public async Task
		does_not_allow_checkpoint_to_point_into_the_middle_of_completed_chunk_when_not_enough_actual_data() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 1500);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"), actualDataSize: 499);

			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task does_not_allow_checkpoint_to_point_into_the_middle_of_scavenged_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 1500, chunkSize: 1000);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"), isScavenged: true,
				actualDataSize: 1000);

			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task old_version_of_chunks_are_removed() {
		File.Create(GetFilePathFor("foo")).Close();
		File.Create(GetFilePathFor("bla")).Close();

		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 350, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000002"));
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000005"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			DbUtil.CreateSingleChunk(config, 3, GetFilePathFor("chunk-000003.000007"));
			DbUtil.CreateOngoingChunk(config, 3, GetFilePathFor("chunk-000003.000008"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("foo")));
			Assert.IsTrue(File.Exists(GetFilePathFor("bla")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000005")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000003.000008")));
			Assert.AreEqual(6, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_checkpoint_is_on_boundary_of_chunk_last_chunk_is_preserved() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000005"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000005")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task
		when_checkpoint_is_on_boundary_of_new_chunk_last_chunk_is_preserved_and_excessive_versions_are_removed_if_present() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateSingleChunk(config, 2, GetFilePathFor("chunk-000002.000000"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000001")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task
		when_checkpoint_is_exactly_on_the_boundary_of_chunk_the_last_chunk_could_be_not_present_but_should_be_created() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_checkpoint_is_exactly_on_the_boundary_of_chunk_the_last_chunk_could_be_present() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateOngoingChunk(config, 2, GetFilePathFor("chunk-000002.000000"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_checkpoint_is_on_boundary_of_new_chunk_and_last_chunk_is_truncated_no_exception_is_thrown() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateSingleChunk(config, 1, GetFilePathFor("chunk-000001.000001"),
				actualDataSize: config.ChunkSize - 10);

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));
			Assert.IsNotNull(await db.Manager.GetInitializedChunk(2, CancellationToken.None));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000002.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test, Ignore("Not valid test now after disabling size validation on ongoing TFChunk ")]
	public async Task
		when_checkpoint_is_on_boundary_of_new_chunk_and_last_chunk_is_truncated_but_not_completed_exception_is_thrown() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 200, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000001"),
				actualSize: config.ChunkSize - 10);

			var ex = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));
			Assert.True(ex?.InnerException is BadChunkInDatabaseException);
		}
	}

	[Test]
	public async Task when_last_chunk_is_scavenged_and_checkpoint_is_on_boundary_of_new_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 300, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateMultiChunk(config, 0, 2, GetFilePathFor("chunk-000000.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsNotNull(await db.Manager.GetInitializedChunk(3, CancellationToken.None));
			Assert.AreEqual(300, db.Config.WriterCheckpoint.Read());

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000003.000000")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_last_chunk_is_scavenged_and_checkpoint_is_at_the_start_of_the_scavenged_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 100, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 3, GetFilePathFor("chunk-000001.000001"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsNotNull(await db.Manager.GetInitializedChunk(4, CancellationToken.None));
			Assert.AreEqual(400, db.Config.WriterCheckpoint.Read());

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000004.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_last_chunk_is_scavenged_and_checkpoint_is_at_the_start_of_the_scavenged_chunk_and_new_chunk_already_exists() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 100, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 3, GetFilePathFor("chunk-000001.000001"));
			DbUtil.CreateSingleChunk(config, 4, GetFilePathFor("chunk-000004.000000"));

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsNotNull(await db.Manager.GetInitializedChunk(4, CancellationToken.None));
			Assert.AreEqual(400, db.Config.WriterCheckpoint.Read());

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000004.000000")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_last_chunk_is_scavenged_and_checkpoint_is_in_the_middle_of_the_scavenged_chunk() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 150, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateMultiChunk(config, 1, 3, GetFilePathFor("chunk-000001.000001"));

			Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open(verifyHash: false));

			Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await db.Manager.GetInitializedChunk(4, CancellationToken.None));
			Assert.AreEqual(150, db.Config.WriterCheckpoint.Read());

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.AreEqual(2, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task temporary_files_are_removed() {
		var config = TFChunkHelper.CreateSizedDbConfig(PathName, 150, chunkSize: 100);
		await using (var db = new TFChunkDb(config)) {
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"));
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000001"));

			File.Create(GetFilePathFor("bla")).Close();
			File.Create(GetFilePathFor("bla.scavenge.tmp")).Close();
			File.Create(GetFilePathFor("bla.tmp")).Close();

			Assert.DoesNotThrowAsync(async () => await db.Open(verifyHash: false));

			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000000.000000")));
			Assert.IsTrue(File.Exists(GetFilePathFor("chunk-000001.000001")));
			Assert.IsTrue(File.Exists(GetFilePathFor("bla")));
			Assert.AreEqual(3, Directory.GetFiles(PathName, "*").Length);
		}
	}

	[Test]
	public async Task when_prelast_chunk_corrupted_throw_hash_validation_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 15000);
		var sink = new TestLogEventSink();
		using (var log = new LoggerConfiguration()
			.WriteTo.Sink(sink)
			.MinimumLevel.Verbose()
			.CreateLogger())
		await using (var db = new TFChunkDb(config, new TFChunkTracker.NoOp(), log)) {
			byte[] contents = new byte[config.ChunkSize];
			for (var i = 0; i < config.ChunkSize; i++) {
				contents[i] = 0;
			}

			/*
			Create a completed chunk and an ongoing chunk
			 */
			DbUtil.CreateSingleChunk(config, 0, GetFilePathFor("chunk-000000.000000"),
				actualDataSize: config.ChunkSize,
				contents: contents);
			DbUtil.CreateOngoingChunk(config, 1, GetFilePathFor("chunk-000001.000000"));
			/**
			Corrupt the prelast completed chunk by modifying bytes of its content
			 */
			using (Stream stream = File.Open(GetFilePathFor("chunk-000000.000000"), FileMode.Open)) {
				var data = new byte[3];
				data[0] = 1;
				data[1] = 2;
				data[2] = 3;
				stream.Position = ChunkHeader.Size + 15; //arbitrary choice of position to modify
				stream.Write(data, 0, data.Length);
			}

			/**
			Exception being thrown in another thread, using the output to check for the exception
			 */
			await db.Open(verifyHash: true);
			//arbitrary wait
			await Task.Delay(2000);
		}

		var thrownException = sink.LogEventReceived.WithTimeout().Result;

		Assert.IsInstanceOf<HashValidationException>(thrownException);

		var output = sink.Output;

		Assert.AreEqual(@"Verification of chunk ""#0-0 (chunk-000000.000000)"" failed, terminating server...",
			output);
	}

	private class TestLogEventSink : ILogEventSink {
		private readonly StringBuilder _output;

		private readonly TaskCompletionSource<Exception> _logEventReceived;
		public Task<Exception> LogEventReceived => _logEventReceived.Task;

		public string Output => _output.ToString();

		public TestLogEventSink() {
			_output = new StringBuilder();
			_logEventReceived = new TaskCompletionSource<Exception>();
		}

		public void Emit(LogEvent logEvent) {
			if (logEvent.Exception is null)
				return;
			_output.Append(logEvent.RenderMessage());
			_logEventReceived.TrySetResult(logEvent.Exception);
		}
	}
}
