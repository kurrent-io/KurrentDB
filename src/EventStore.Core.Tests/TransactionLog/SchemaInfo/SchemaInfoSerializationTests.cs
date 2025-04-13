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
	public void ser_de_schema_exists() {
		var expectedPayload = new byte[] { 1, 2, 3 };
		var version = Guid.NewGuid();
		var expectedSchema = new Data.SchemaInfo(Data.SchemaInfo.SchemaDataFormat.Json, version);

		var serialized = new ReadOnlySequence<byte>(PrepareLogRecord.WrapSchemaInfo(expectedSchema, expectedPayload));

		var reader = new SequenceReader(serialized);
		var actualSchema = Data.SchemaInfo.Read(ref reader);
		var actualPayload = reader.RemainingSequence.ToArray();

		Assert.AreEqual(expectedSchema, actualSchema);
		Assert.AreEqual(expectedPayload, actualPayload);
	}

	[Test]
	public void ser_de_schema_absent() {
		var expectedPayload = new byte[] { 1, 2, 3 };
		var serialized = new ReadOnlySequence<byte>(PrepareLogRecord.WrapSchemaInfo(Data.SchemaInfo.None, expectedPayload));

		var reader = new SequenceReader(serialized);
		var actualPayload = reader.RemainingSequence.ToArray();

		Assert.AreEqual(expectedPayload, actualPayload);
	}
}
