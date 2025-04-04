using System.Threading;

namespace EventStore.TestClient.Commands {
	internal class ExitProcessor : ICmdProcessor {
		private readonly CancellationTokenSource _cancellationTokenSource;

		public ExitProcessor(CancellationTokenSource cancellationTokenSource) {
			_cancellationTokenSource = cancellationTokenSource;
		}

		public string Usage {
			get { return Keyword; }
		}

		public string Keyword {
			get { return "EXIT"; }
		}

		public bool Execute(CommandProcessorContext context, string[] args) {
			context.Log.Information("Exiting...");
			_cancellationTokenSource.Cancel();
			return true;
		}
	}
}
