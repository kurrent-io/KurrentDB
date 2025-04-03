// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Exceptions;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Validation;

[TestFixture]
public class when_validating_tfchunkdb_without_previous_files : SpecificationWithDirectory {
	[Test]
	public async Task with_a_writer_checksum_of_nonzero_and_no_files_a_corrupted_database_exception_is_thrown() {
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 500));
		var exc = Assert.ThrowsAsync<CorruptDatabaseException>(async () => await db.Open());
		Assert.IsInstanceOf<ChunkNotFoundException>(exc.InnerException);
	}

	[Test]
	public async Task with_a_writer_checksum_of_zero_and_no_files_is_valid() {
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
		Assert.DoesNotThrowAsync(async () => await db.Open());
	}
}
