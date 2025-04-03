// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
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
