// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_creating_chunked_transaction_file_reader : SpecificationWithDirectory {
	[Test]
	public void a_null_db_config_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new TFChunkReader(null, new InMemoryCheckpoint(0)));
	}

	[Test]
	public async Task a_null_checkpoint_throws_argument_null_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		await using var db = new TFChunkDb(config);
		Assert.Throws<ArgumentNullException>(() => new TFChunkReader(db, null));
	}
}
