// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.Bus;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RedactionService;

public abstract class SwitchChunkTests<TLogFormat, TStreamId> : RedactionServiceTestFixture<TLogFormat,TStreamId> {
	private const string StreamId = nameof(SwitchChunkTests<TLogFormat, TStreamId>);
	protected const string FakeChunk = "fake_chunk.tmp";
	private Guid _lockId;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		// the writes below create 3 chunks for both V2 & V3 log formats
		await WriteSingleEvent(StreamId, 0, new string('0', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 1, new string('1', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 2, new string('2', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 3, new string('3', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 4, new string('4', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 5, new string('5', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 6, new string('6', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 7, new string('7', 50), retryOnFail: true, token: token);
		await WriteSingleEvent(StreamId, 8, new string('8', 50), retryOnFail: true, token: token);

		var writerPos = Writer.Position;
		var chunk = Path.GetFileName((await Db.Manager.GetInitializedChunkFor(writerPos, token)).LocalFileName);
		var chunkNum = Db.Manager.FileSystem.LocalNamingStrategy.GetIndexFor(chunk);
		Assert.AreEqual(2, chunkNum);

		// create an empty file that can be used in tests that require the target or new chunk files to exist
		using var fs = File.CreateText(Path.Combine(PathName, FakeChunk));
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		var e = new TcsEnvelope<RedactionMessage.AcquireChunksLockCompleted>();
		RedactionService.Handle(new RedactionMessage.AcquireChunksLock(e));
		var result = await e.Task;
		Assert.NotNull(result);
		Assert.AreEqual(AcquireChunksLockResult.Success, result.Result);
		_lockId = result.AcquisitionId;
	}

	[TearDown]
	public override async Task TearDown() {
		var e = new TcsEnvelope<RedactionMessage.ReleaseChunksLockCompleted>();
		RedactionService.Handle(new RedactionMessage.ReleaseChunksLock(e, _lockId));
		await e.Task;

		await base.TearDown();
	}

	protected string GetChunk(int chunkNum, int chunkVersion, bool fullPath = false) {
		var chunkPath = Db.Manager.FileSystem.LocalNamingStrategy.GetFilenameFor(chunkNum, chunkVersion);
		return fullPath ? chunkPath : Path.GetFileName(chunkPath);
	}

	protected async Task<RedactionMessage.SwitchChunkCompleted> SwitchChunk(string targetChunk, string newChunk) {
		var e = new TcsEnvelope<RedactionMessage.SwitchChunkCompleted>();
		await RedactionService.As<IAsyncHandle<RedactionMessage.SwitchChunk>>()
			.HandleAsync(new(e, _lockId, targetChunk, newChunk), CancellationToken.None);
		return await e.Task;
	}
}
