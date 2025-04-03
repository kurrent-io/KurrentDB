// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_creating_chunked_transaction_file_database {
	[Test]
	public void a_null_config_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => new TFChunkDb(null));
	}
}
