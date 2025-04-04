using System;
using System.Threading;

namespace EventStore.Projections.Core.Services.Management {
	public class TimeoutScheduler : ISingletonTimeoutScheduler {
		private Entry _current;

		private class Entry {
			public Action Action;
			public int Timeout;
		}

		public void Tick() {
			var entry = _current;
			if (entry != null) {
				entry.Timeout -= 100;
				if (entry.Timeout <= 0) {
					if (Interlocked.CompareExchange(ref _current, null, entry) == entry) {
						entry.Action();
					}
				}
			}
		}

		public void Schedule(int timeout, Action action) {
			var entry = new Entry { Action = action, Timeout = timeout };
			Interlocked.Exchange(ref _current, entry);
		}
	}
}
