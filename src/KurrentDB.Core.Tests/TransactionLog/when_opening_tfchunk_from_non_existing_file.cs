// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using KurrentDB.Core.Transforms;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.TransactionLog;

[TestFixture]
public class when_opening_tfchunk_from_non_existing_file : SpecificationWithFile {
	[Test]
	public void it_should_throw_a_file_not_found_exception() {
		Assert.ThrowsAsync<CorruptDatabaseException>(async () => await TFChunk.FromCompletedFile(new ChunkLocalFileSystem(Path.GetDirectoryName(Filename)), Filename, verifyHash: true,
			unbufferedRead: false, reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default));
	}
}
