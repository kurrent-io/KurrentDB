using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace EventStore.TestClient {
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
}
