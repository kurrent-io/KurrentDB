// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Testing;

public sealed class Disposables : IAsyncDisposable {
	readonly IList<object> _disposables = [];

	public T RegisterAsync<T>(T item) where T : IAsyncDisposable {
		_disposables.Add(item);
		return item;
	}

	public T Register<T>(T item) where T : IDisposable {
		_disposables.Add(item);
		return item;
	}

	public async ValueTask DisposeAsync() {
		foreach (var disposable in _disposables.Reverse()) {
			if (disposable is IAsyncDisposable x) {
				await x.DisposeAsync();
			} else if (disposable is IDisposable y) {
				y.Dispose();
			}
		}
	}
}

public static class DisposablesExtensions {
	public static T DisposeAsyncWith<T>(this T item, Disposables disposables) where T : IAsyncDisposable =>
		disposables.RegisterAsync(item);

	public static T DisposeWith<T>(this T item, Disposables disposables) where T : IDisposable =>
		disposables.Register(item);
}
