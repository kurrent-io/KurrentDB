// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using DotNext.IO;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.SchemaInfo;

public class SchemaInfoSerializationTests {
	[Test]
	public void ser_de_schema_exist() {
		var payload = new byte[] { 1, 2, 3 };
		var version = Guid.NewGuid();
		var schema = new Data.SchemaInfo(Data.SchemaInfo.SchemaDataFormat.Json, version);

		var serialized = new ReadOnlySequence<byte>(PrepareLogRecord.WrapSchemaInfo(schema, payload));

		var reader = new SequenceReader(serialized);
		var actual = Data.SchemaInfo.Read(ref reader);

		Assert.AreEqual(schema, actual);
	}
}
