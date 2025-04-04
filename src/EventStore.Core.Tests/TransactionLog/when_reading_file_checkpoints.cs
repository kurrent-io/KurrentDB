// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.TransactionLog.Checkpoint;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_reading_file_checkpoints : SpecificationWithFile {
	[Test]
	public void mem_mapped_file_checkpoint_can_be_read_as_file_checkpoint() {
		var memoryMapped = new MemoryMappedFileCheckpoint(Filename);
		memoryMapped.Write(0xDEAD);
		memoryMapped.Close(flush: true);

		var fileCheckpoint = new FileCheckpoint(Filename);
		var read = fileCheckpoint.Read();
		fileCheckpoint.Close(flush: true);
		Assert.AreEqual(0xDEAD, read);
	}

	[Test]
	public void file_checkpoint_can_be_read_as_mem_mapped_file_checkpoint() {
		var fileCheckpoint = new FileCheckpoint(Filename);
		fileCheckpoint.Write(0xDEAD);
		fileCheckpoint.Close(flush: true);

		var memoryMapped = new MemoryMappedFileCheckpoint(Filename);
		var read = memoryMapped.Read();
		memoryMapped.Close(flush: true);
		Assert.AreEqual(0xDEAD, read);
	}
}
