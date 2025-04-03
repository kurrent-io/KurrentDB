// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Checkpoint;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal class InterceptorCheckpoint : ICheckpoint {
	private readonly ICheckpoint _wrapped;
	private readonly List<long> _values = new();
	private readonly object _lock = new();

	public IEnumerable<long> Values {
		get {
			lock (_lock) {
				return _values.ToArray();
			}
		}
	}

	public InterceptorCheckpoint(ICheckpoint wrapped) {
		_wrapped = wrapped;
	}

	public string Name => _wrapped.Name;

	public event Action<long> Flushed {
		add {
			_wrapped.Flushed += value;
		}
		remove {
			_wrapped.Flushed -= value;
		}
	}

	public long Read() => _wrapped.Read();
	public long ReadNonFlushed() => _wrapped.ReadNonFlushed();

	public void Write(long checkpoint) {
		_wrapped.Write(checkpoint);
		lock (_lock) {
			_values.Add(checkpoint);
		}
	}

	public void Flush() => _wrapped.Flush();
	public void Close(bool flush) => _wrapped.Close(flush: flush);
}
