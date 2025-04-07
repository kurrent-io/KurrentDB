// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
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
	public void a_null_checkpoint_throws_argument_null_exception() {
		var config = TFChunkHelper.CreateDbConfig(PathName, 0);
		using var db = new TFChunkDb(config);
		Assert.Throws<ArgumentNullException>(() => new TFChunkReader(db, null));
	}
}
