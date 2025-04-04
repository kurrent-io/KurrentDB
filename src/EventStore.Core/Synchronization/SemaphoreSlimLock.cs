// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;

namespace EventStore.Core.Synchronization;

public class SemaphoreSlimLock : IDisposable {
	private readonly SemaphoreSlim _semaphoreSlim;
	private readonly object _idLock = new();
	private Guid _acquisitionId = Guid.Empty;

	public Guid CurrentAcquisitionId {
		get {
			lock (_idLock) {
				return _acquisitionId;
			}
		}
	}

	public SemaphoreSlimLock() {
		_semaphoreSlim = new SemaphoreSlim(1, 1);
	}

	public bool TryAcquire(out Guid acquisitionId) {
		if (!_semaphoreSlim.Wait(TimeSpan.Zero)) {
			acquisitionId = Guid.Empty;
			return false;
		}

		acquisitionId = Guid.NewGuid();
		lock (_idLock) {
			_acquisitionId = acquisitionId;
		}

		return true;
	}

	public bool TryRelease(Guid acquisitionId) {
		lock (_idLock) {
			if (_acquisitionId == Guid.Empty)
				return false;

			if (_acquisitionId != acquisitionId)
				return false;

			_acquisitionId = Guid.Empty;
		}

		_semaphoreSlim.Release();
		return true;
	}

	public void Dispose() {
		GC.SuppressFinalize(this);
		_semaphoreSlim?.Dispose();
	}
}
