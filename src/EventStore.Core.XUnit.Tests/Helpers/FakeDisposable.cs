using System;

namespace EventStore.Core.XUnit.Tests.Helpers {
	public class FakeDisposable : IDisposable {
		private readonly Action _disposeAction;

		public FakeDisposable(Action disposeAction) {
			_disposeAction = disposeAction;
		}

		public void Dispose() => _disposeAction?.Invoke();
	}
}
