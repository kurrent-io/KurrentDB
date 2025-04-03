// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal class ReplicationInterceptor : WriterInterceptor {
	private record DataChunkInterceptionInfo {
		public long MaxLogPosition { get; init; }
	}

	private static readonly DataChunkInterceptionInfo DefaultDataInfo = new() {
		MaxLogPosition = long.MaxValue
	};

	private record RawChunkInterceptionInfo {
		public int ChunkStartNumber { get; init; }
		public int ChunkEndNumber { get; init; }
		public int MaxRawPosition { get; init; }
	}

	private static readonly RawChunkInterceptionInfo DefaultRawInfo = new() {
		ChunkStartNumber = -1,
		ChunkEndNumber = -1,
		MaxRawPosition = -1
	};


	private DataChunkInterceptionInfo _dataInfo = DefaultDataInfo;
	private RawChunkInterceptionInfo _rawInfo = DefaultRawInfo;

	private readonly object _lock = new();

	public ReplicationInterceptor(ISubscriber subscriber) : base(subscriber) { }

	private void PauseIfConditionsAreMet(Message message, int bytesToAdd) {
		if (Paused)
			return;

		switch (message)
		{
			case ReplicationMessage.DataChunkBulk dataChunkBulk:
				if (dataChunkBulk.SubscriptionPosition + dataChunkBulk.DataBytes.Length + bytesToAdd >
				    _dataInfo.MaxLogPosition)
					Pause();
				break;
			case ReplicationMessage.RawChunkBulk rawChunkBulk:
				if (rawChunkBulk.ChunkStartNumber == _rawInfo.ChunkStartNumber &&
				    rawChunkBulk.ChunkEndNumber == _rawInfo.ChunkEndNumber &&
				    rawChunkBulk.RawPosition + rawChunkBulk.RawBytes.Length + bytesToAdd > _rawInfo.MaxRawPosition)
					Pause();
				break;
		}
	}
	protected override void Process(Message message) {
		lock (_lock) {
			PauseIfConditionsAreMet(message, bytesToAdd: 0);
			base.Process(message);
			PauseIfConditionsAreMet(message, bytesToAdd: 1);
		}
	}

	public override void Resume() {
		lock (_lock) {
			_dataInfo = DefaultDataInfo;
			_rawInfo = DefaultRawInfo;
			base.Resume();
		}
	}

	public void ResumeUntil(long maxLogPosition) {
		lock (_lock) {
			_dataInfo = new DataChunkInterceptionInfo {
				MaxLogPosition = maxLogPosition
			};
			base.Resume();
		}
	}

	public void ResumeUntil(int rawChunkStartNumber, int rawChunkEndNumber, int maxRawPosition) {
		lock (_lock) {
			_rawInfo = new RawChunkInterceptionInfo {
				ChunkStartNumber = rawChunkStartNumber,
				ChunkEndNumber = rawChunkEndNumber,
				MaxRawPosition = maxRawPosition
			};
			base.Resume();
		}
	}

	public void Reset(bool pauseReplication = false) {
		lock (_lock) {
			_dataInfo = DefaultDataInfo;
			_rawInfo = DefaultRawInfo;

			base.Reset();
			if (pauseReplication)
				base.Pause();
		}
	}
}
