// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_deleting_stream_spanning_through_multiple_chunks_in_db_with_other_streams_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteSingleEvent("ES1", 0, new string('.', 3000), token: token);
		await WriteSingleEvent("ES1", 1, new string('.', 3000), token: token);
		await WriteSingleEvent("ES2", 0, new string('.', 3000), token: token);

		await WriteSingleEvent("ES", 0, new string('.', 3000), retryOnFail: true, token: token); // chunk 2
		await WriteSingleEvent("ES", 1, new string('.', 3000), token: token);
		await WriteSingleEvent("ES1", 2, new string('.', 3000), token: token);

		await WriteSingleEvent("ES2", 1, new string('.', 3000), retryOnFail: true, token: token); // chunk 3
		await WriteSingleEvent("ES1", 3, new string('.', 3000), token: token);
		await WriteSingleEvent("ES1", 4, new string('.', 3000), token: token);

		await WriteSingleEvent("ES2", 2, new string('.', 3000), retryOnFail: true, token: token); // chunk 4
		await WriteSingleEvent("ES", 2, new string('.', 3000), token: token);
		await WriteSingleEvent("ES", 3, new string('.', 3000), token: token);

		await WriteDelete("ES1", token);
	}

	[Test]
	public async Task indicate_that_stream_is_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES1", CancellationToken.None));
	}

	[Test]
	public async Task indicate_that_nonexisting_stream_with_same_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("XXX", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_nonexisting_stream_with_different_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("XXXX", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_existing_stream_with_same_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES2", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_existing_stream_with_different_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES", CancellationToken.None), Is.False);
	}
}
