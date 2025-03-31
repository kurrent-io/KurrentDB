// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.Core.Transforms;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkManagerForExecutor<TStreamId> : IChunkManagerForChunkExecutor<TStreamId, ILogRecord, TFChunk> {
	private readonly ILogger _logger;
	private readonly TFChunkManager _manager;
	private readonly TFChunkDbConfig _dbConfig;
	private readonly DbTransformManager _transformManager;

	public ChunkManagerForExecutor(ILogger logger, TFChunkManager manager, TFChunkDbConfig dbConfig, DbTransformManager transformManager) {
		_logger = logger;
		_manager = manager;
		_dbConfig = dbConfig;
		_transformManager = transformManager;
	}

	public async ValueTask<IChunkWriterForExecutor<TStreamId, ILogRecord, TFChunk>> CreateChunkWriter(
		IChunkReaderForExecutor<TStreamId, ILogRecord> sourceChunk,
		CancellationToken token)
		=> await ChunkWriterForExecutor<TStreamId>.CreateAsync(_logger, _manager.FileSystem, _dbConfig, sourceChunk,
			_transformManager, token);

	public IChunkReaderForExecutor<TStreamId, ILogRecord> GetChunkReaderFor(long position) {
		var tfChunk = _manager.GetChunkFor(position);
		return new ChunkReaderForExecutor<TStreamId>(tfChunk);
	}

	public async ValueTask<string> SwitchInTempChunk(
		TFChunk chunk,
		CancellationToken token) {

		var tfChunk = await _manager.SwitchInTempChunk(
			chunk: chunk,
			verifyHash: false,
			removeChunksWithGreaterNumbers: false,
			token);

		if (tfChunk is null) {
			throw new Exception("Unexpected error: new chunk is null after switch");
		}

		return tfChunk.ChunkLocator;
	}
}
