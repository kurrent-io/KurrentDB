// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace EventStore.TestClient;

internal class TestClientHostedService : IHostedService {
	private readonly Client _client;
	private readonly CancellationTokenSource _stopped;
	private readonly TaskCompletionSource<int> _exitCode;

	public Task<int> Exited => _exitCode.Task;

	public CancellationToken CancellationToken => _stopped.Token;

	public TestClientHostedService(ClientOptions options) {
		_exitCode = new TaskCompletionSource<int>();
		_stopped = new CancellationTokenSource();
		_stopped.Token.Register(() => _exitCode.TrySetResult(0));
		_client = new Client(options, _stopped);
	}
	public Task StartAsync(CancellationToken cancellationToken) {
		cancellationToken.Register(_stopped.Cancel);
		return Task.Run(() => {
			_exitCode.SetResult(_client.Run(cancellationToken));
			if (!_client.InteractiveMode) {
				_stopped.Cancel();
			}
		}, _stopped.Token);
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		_stopped.Cancel();
		return Task.CompletedTask;
	}
}
