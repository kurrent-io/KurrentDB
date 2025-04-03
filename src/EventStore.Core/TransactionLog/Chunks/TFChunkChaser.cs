// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Checkpoint;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkChaser : ITransactionFileChaser {
	public ICheckpoint Checkpoint {
		get { return _chaserCheckpoint; }
	}

	private readonly TFChunkDb _db;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly ICheckpoint _chaserCheckpoint;
	private TFChunkReader _reader;

	public TFChunkChaser(TFChunkDb db, IReadOnlyCheckpoint writerCheckpoint, ICheckpoint chaserCheckpoint) {
		Ensure.NotNull(db, "dbConfig");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");
		Ensure.NotNull(chaserCheckpoint, "chaserCheckpoint");

		_db = db;
		_writerCheckpoint = writerCheckpoint;
		_chaserCheckpoint = chaserCheckpoint;
	}

	public void Open() {
		_reader = new TFChunkReader(_db, _writerCheckpoint, _chaserCheckpoint.Read());
	}

	public async ValueTask<SeqReadResult> TryReadNext(CancellationToken token) {
		var res = await _reader.TryReadNext(token);
		if (res.Success)
			_chaserCheckpoint.Write(res.RecordPostPosition);
		else
			_chaserCheckpoint.Write(_reader.CurrentPosition);

		return res;
	}

	public void Dispose() {
		Close();
	}

	public void Close() {
		Flush();
	}

	public void Flush() {
		_chaserCheckpoint.Flush();
	}
}
