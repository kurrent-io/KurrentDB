// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http;

public class AsyncStreamCopier<T> {
	public Exception Error { get; private set; }
	public readonly T AsyncState;

	private readonly byte[] _buffer = new byte[4096];
	private readonly Stream _input;
	private readonly Stream _output;
	private readonly Action<AsyncStreamCopier<T>> _onCompleted;

	public AsyncStreamCopier(Stream input, Stream output, T state, Action<AsyncStreamCopier<T>> onCompleted) {
		Ensure.NotNull(input, "input");
		Ensure.NotNull(output, "output");
		Ensure.NotNull(onCompleted, "onCompleted");

		_input = input;
		_output = output;
		_onCompleted = onCompleted;

		AsyncState = state;
		Error = null;
	}

	public void Start() {
		GetNextChunk();
	}

	private void GetNextChunk() {
		try {
			_input.BeginRead(_buffer, 0, _buffer.Length, InputReadCompleted, null);
		} catch (Exception e) {
			Error = e;
			OnCompleted();
		}
	}

	private void InputReadCompleted(IAsyncResult ar) {
		try {
			int bytesRead = _input.EndRead(ar);
			if (bytesRead <= 0) // mono can return -1
			{
				OnCompleted();
				return;
			}

			_output.BeginWrite(_buffer, 0, bytesRead, OutputWriteCompleted, null);
		} catch (Exception e) {
			Error = e;
			OnCompleted();
		}
	}

	private void OutputWriteCompleted(IAsyncResult ar) {
		try {
			_output.EndWrite(ar);
			GetNextChunk();
		} catch (Exception e) {
			Error = e;
			OnCompleted();
		}
	}

	private void OnCompleted() {
		if (_onCompleted != null)
			_onCompleted(this);
	}
}
