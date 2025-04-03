// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.Checkpoint;

public class MemoryMappedFileCheckpoint : ICheckpoint {
	public string Name {
		get { return _name; }
	}

	private readonly string _filename;
	private readonly string _name;
	private readonly FileStream _fileStream;
	private readonly MemoryMappedFile _file;
	private long _last;
	private long _lastFlushed;
	private readonly MemoryMappedViewAccessor _accessor;

	public MemoryMappedFileCheckpoint(string filename) : this(filename, Guid.NewGuid().ToString()) {
	}

	public MemoryMappedFileCheckpoint(string filename, string name, bool mustExist = false,
		long initValue = 0) {
		_filename = filename;
		_name = name;
		var old = File.Exists(_filename);
		_fileStream = new FileStream(_filename,
			mustExist ? FileMode.Open : FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.ReadWrite);
		_fileStream.SetLength(sizeof(long));
		_file = MemoryMappedFile.CreateFromFile(_fileStream,
			null,
			sizeof(long),
			MemoryMappedFileAccess.ReadWrite,
			HandleInheritability.None,
			false);
		_accessor = _file.CreateViewAccessor(0, sizeof(long));

		if (old)
			_last = _lastFlushed = _accessor.ReadInt64(0);
		else {
			_last = initValue;
			Flush();
		}
	}

	public void Close(bool flush) {
		if (flush)
			Flush();
		_accessor.Dispose();
		_file.Dispose();
	}

	public void Write(long checkpoint) {
		Interlocked.Exchange(ref _last, checkpoint);
	}

	public void Flush() {
		var last = Interlocked.Read(ref _last);
		if (last == _lastFlushed)
			return;

		_accessor.Write(0, last);
		_accessor.Flush();

		_fileStream.FlushToDisk();

		Interlocked.Exchange(ref _lastFlushed, last);

		OnFlushed(last);
	}

	public long Read() {
		return Interlocked.Read(ref _lastFlushed);
	}

	public long ReadNonFlushed() {
		return Interlocked.Read(ref _last);
	}

	public event Action<long> Flushed;

	private void OnFlushed(long obj) {
		var onFlushed = Flushed;
		if (onFlushed != null)
			onFlushed.Invoke(obj);
	}
}
