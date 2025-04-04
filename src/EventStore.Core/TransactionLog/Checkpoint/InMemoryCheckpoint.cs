// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;

namespace EventStore.Core.TransactionLog.Checkpoint;

public class InMemoryCheckpoint : ICheckpoint {
	public string Name {
		get { return _name; }
	}

	private long _last;
	private long _lastFlushed;
	private readonly string _name;

	public InMemoryCheckpoint(long initialValue) : this(Guid.NewGuid().ToString(), initialValue) {
	}

	public InMemoryCheckpoint() : this(Guid.NewGuid().ToString(), 0) {
	}

	public InMemoryCheckpoint(string name, long initValue = 0) {
		_last = initValue;
		_lastFlushed = initValue;
		_name = name;
	}

	public void Write(long checkpoint) {
		Interlocked.Exchange(ref _last, checkpoint);
	}

	public long Read() {
		return Interlocked.Read(ref _lastFlushed);
	}

	public long ReadNonFlushed() {
		return Interlocked.Read(ref _last);
	}

	public void Flush() {
		var last = Interlocked.Read(ref _last);
		if (last == _lastFlushed)
			return;

		Interlocked.Exchange(ref _lastFlushed, last);

		OnFlushed(last);
	}

	public event Action<long> Flushed;

	private void OnFlushed(long obj) {
		var onFlushed = Flushed;
		if (onFlushed != null)
			onFlushed.Invoke(obj);
	}

	public void Close(bool flush) {
		if (flush)
			Flush();
	}
}
