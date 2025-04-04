// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_reading_physical_bytes_bulk_from_a_chunk : SpecificationWithDirectory {
	[Test]
	public async Task the_file_will_not_be_deleted_until_reader_released() {
		var chunk = await TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
		await chunk.Complete(CancellationToken.None);
		using (var reader = await chunk.AcquireRawReader(CancellationToken.None)) {
			chunk.MarkForDeletion();
			var buffer = new byte[1024];
			var result = await reader.ReadNextBytes(buffer, CancellationToken.None);
			Assert.IsFalse(result.IsEOF);
			Assert.AreEqual(1024, result.BytesRead);
		}

		chunk.WaitForDestroy(5000);
	}

	[Test]
	public async Task a_raw_read_on_new_file_cannot_be_performed() {
		var chunk = await TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);

		Assert.ThrowsAsync<Exception>(async () => {
			await chunk.AcquireRawReader(CancellationToken.None);
		});

		chunk.MarkForDeletion();
		chunk.WaitForDestroy(5000);
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task a_raw_read_on_newly_completed_file_can_be_performed(bool cached) {
		var chunk = await TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 2000);
		await chunk.Complete(CancellationToken.None);

		Assert.True(chunk.IsCached);
		if (!cached) {
			await chunk.UnCacheFromMemory(CancellationToken.None);
			Assert.True(chunk.IsUncached);
		}

		using (var reader = await chunk.AcquireRawReader(CancellationToken.None)) {
			var buffer = new byte[1024];
			var result = await reader.ReadNextBytes(buffer, CancellationToken.None);
			Assert.IsFalse(result.IsEOF);
			Assert.AreEqual(1024, result.BytesRead);

			var header = new ChunkHeader(buffer);
			Assert.AreEqual(2000, header.ChunkSize);
		}

		await chunk.UnCacheFromMemory(CancellationToken.None);
		Assert.True(chunk.IsUncached);

		chunk.MarkForDeletion();
		chunk.WaitForDestroy(5000);
	}

	/*
			[Test]
			public void a_read_on_scavenged_chunk_includes_map()
			{
				var chunk = TFChunk.CreateNew(GetFilePathFor("afile"), 200, 0, 0, isScavenged: true, inMem: false, unbuffered: false, writethrough: false);
				chunk.CompleteScavenge(new [] {new PosMap(0, 0), new PosMap(1,1) }, false);
				using (var reader = chunk.AcquireRawReader())
				{
					var buffer = new byte[1024];
					var result = reader.ReadNextBytes(1024, buffer);
					Assert.IsFalse(result.IsEOF);
					Assert.AreEqual(ChunkHeader.Size + ChunkHeader.Size + 2 * PosMap.FullSize, result.BytesRead);
				}
				chunk.MarkForDeletion();
				chunk.WaitForDestroy(5000);
			}

			[Test]
			public void a_read_past_end_of_completed_chunk_does_include_header_or_footer()
			{
				var chunk = TFChunk.CreateNew(GetFilePathFor("File1"), 300, 0, 0, isScavenged: false, inMem: false, unbuffered: false, writethrough: false);
				chunk.Complete();
				using (var reader = chunk.AcquireRawReader())
				{
					var buffer = new byte[1024];
					var result = reader.ReadNextBytes(1024, buffer);
					Assert.IsTrue(result.IsEOF);
					Assert.AreEqual(ChunkHeader.Size + ChunkFooter.Size, result.BytesRead); //just header + footer = 256
				}
				chunk.MarkForDeletion();
				chunk.WaitForDestroy(5000);
			}
	*/

	[Test]
	public async Task if_asked_for_more_than_buffer_size_will_only_read_buffer_size() {
		var chunk = await TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 3000);
		await chunk.Complete(CancellationToken.None);
		using (var reader = await chunk.AcquireRawReader(CancellationToken.None)) {
			var buffer = new byte[1024];
			var result = await reader.ReadNextBytes(buffer, CancellationToken.None);
			Assert.IsFalse(result.IsEOF);
			Assert.AreEqual(1024, result.BytesRead);
		}

		chunk.MarkForDeletion();
		chunk.WaitForDestroy(5000);
	}

	[Test]
	public async Task a_read_past_eof_returns_eof_and_no_footer() {
		var chunk = await TFChunkHelper.CreateNewChunk(GetFilePathFor("file1"), 300);
		await chunk.Complete(CancellationToken.None);
		using (var reader = await chunk.AcquireRawReader(CancellationToken.None)) {
			var buffer = new byte[8092];
			var result = await reader.ReadNextBytes(buffer, CancellationToken.None);
			Assert.IsTrue(result.IsEOF);
			Assert.AreEqual(4096, result.BytesRead); //does not includes header and footer space
		}

		chunk.MarkForDeletion();
		chunk.WaitForDestroy(5000);
	}
}
