// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using KurrentDB.Common.Utils;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core;

/// <summary>
/// Stops two servers running on the same database directory, by holding an exclusive handle on a lock file
/// inside it for as long as the database is open. On Release the handle is closed and the file remains.
/// Can be Acquired and Released on different threads.
/// </summary>
public sealed class ExclusiveDbLock : IDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ExclusiveDbLock>();

	public const string LockFileName = "kurrent.lock";

	public readonly string LockFilePath;

	// For coordinating operations, not the lock that this ExclusiveDbLock represents.
	private readonly Lock _operationLock = new();

	private FileStream _lockFile;

	public bool IsAcquired {
		get {
			lock (_operationLock)
				return _lockFile is not null;
		}
	}

	public ExclusiveDbLock(string dbPath) {
		Ensure.NotNullOrEmpty(dbPath, nameof(dbPath));
		LockFilePath = Path.Combine(dbPath, LockFileName);
	}

	public bool Acquire() {
		lock (_operationLock) {
			if (_lockFile is not null)
				throw new InvalidOperationException($"DB lock '{LockFilePath}' is already acquired by this process.");

			try {
				_lockFile = new FileStream(
					path: LockFilePath,
					mode: FileMode.OpenOrCreate,
					access: FileAccess.ReadWrite,
					share: FileShare.None,
					bufferSize: 1,
					options: FileOptions.None);
				return true;
			} catch (DirectoryNotFoundException ex) {
				Log.Error(
					ex,
					"Could not take the exclusive lock on the database at {lockFile}: " +
					"the directory is not there.",
					LockFilePath);
			} catch (IOException ex) {
				Log.Debug(
					ex,
					"Could not take the exclusive lock on the database at {lockFile}. " +
					"Another server may be holding the lock.",
					LockFilePath);
			} catch (UnauthorizedAccessException ex) {
				Log.Error(
					ex,
					"Could not take the exclusive lock on the database at {lockFile}.",
					LockFilePath);
			}

			return false;
		}
	}

	public void Release() {
		lock (_operationLock) {
			if (_lockFile is null)
				throw new InvalidOperationException($"DB lock '{LockFilePath}' was not previously acquired by this process.");

			// closing the handle is what releases it, and any thread may do that
			_lockFile.Dispose();
			_lockFile = null;
		}
	}

	/// <summary>
	/// Lets go of the lock if it is held. Unlike <see cref="Release"/>, disposing one that was never
	/// acquired, or disposing twice, does nothing.
	/// </summary>
	public void Dispose() {
		lock (_operationLock) {
			_lockFile?.Dispose();
			_lockFile = null;
		}
	}
}
