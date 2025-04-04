// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_deleting_stream_with_1_hash_collision_and_1_stream_with_other_hash_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteSingleEvent("S1", 0, "bla1", token: token);
		await WriteSingleEvent("S1", 1, "bla1", token: token);
		await WriteSingleEvent("S2", 0, "bla1", token: token);
		await WriteSingleEvent("S2", 1, "bla1", token: token);
		await WriteSingleEvent("S1", 2, "bla1", token: token);
		await WriteSingleEvent("SSS", 0, "bla1", token: token);

		await WriteDelete("S1", token);
	}

	[Test]
	public async Task indicate_that_stream_is_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("S1", CancellationToken.None));
	}

	[Test]
	public async Task indicate_that_other_stream_with_same_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("S2", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_other_stream_with_different_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("SSS", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_not_existing_stream_with_same_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("XX", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_not_existing_stream_with_different_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("XXX", CancellationToken.None), Is.False);
	}
}
