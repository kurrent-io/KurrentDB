// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.Checkpoint;

namespace KurrentDB.Core.TransactionLog.Chunks;

public class TFChunkChaser : ITransactionFileChaser, IReadCursor {
	public ICheckpoint Checkpoint {
		get { return _chaserCheckpoint; }
	}

	private readonly TFChunkDb _db;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly ICheckpoint _chaserCheckpoint;
	private TFChunkReader _reader;
	private long _position;

	public TFChunkChaser(TFChunkDb db, IReadOnlyCheckpoint writerCheckpoint, ICheckpoint chaserCheckpoint) {
		Ensure.NotNull(db, "dbConfig");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");
		Ensure.NotNull(chaserCheckpoint, "chaserCheckpoint");

		_db = db;
		_writerCheckpoint = writerCheckpoint;
		_chaserCheckpoint = chaserCheckpoint;
	}

	long IReadCursor.Position {
		get => _position;
		set => _position = value;
	}

	public void Open() {
		_reader = new TFChunkReader(_db, _writerCheckpoint);
		_position = _chaserCheckpoint.Read();
	}

	public async ValueTask<SeqReadResult> TryReadNext(CancellationToken token) {
		var res = await _reader.TryReadNext(this, token);
		if (res.Success)
			_chaserCheckpoint.Write(res.RecordPostPosition);
		else
			_chaserCheckpoint.Write(_position);

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
