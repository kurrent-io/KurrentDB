// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

#nullable enable

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

class FakeAsyncStreamReader {
	public static FakeAsyncStreamReader<T> Create<T>(IAsyncEnumerable<T> inner) => new(inner);
}

class FakeAsyncStreamReader<T> : IAsyncStreamReader<T> {
	private readonly IAsyncEnumerator<T> _inner;

	public FakeAsyncStreamReader(IAsyncEnumerable<T> inner) {
		_inner = inner.GetAsyncEnumerator();
	}

	public T Current => _inner.Current;

	public async Task<bool> MoveNext(CancellationToken cancellationToken) {
		return await _inner.MoveNextAsync();
	}
}
