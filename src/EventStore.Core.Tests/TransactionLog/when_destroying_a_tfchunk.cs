// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_destroying_a_tfchunk : SpecificationWithFile {
	private TFChunk _chunk;

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_chunk = await TFChunkHelper.CreateNewChunk(Filename, 1000);
		_chunk.MarkForDeletion();
	}

	[Test]
	public void the_file_is_deleted() {
		Assert.IsFalse(File.Exists(Filename));
	}
}
