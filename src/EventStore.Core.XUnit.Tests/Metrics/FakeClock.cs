using EventStore.Core.Time;

namespace EventStore.Core.XUnit.Tests.Metrics {
	internal class FakeClock : IClock {
		public Instant Now => Instant.FromSeconds(SecondsSinceEpoch);

		public long SecondsSinceEpoch { get; set; }

		public void AdvanceSeconds(long seconds) {
			SecondsSinceEpoch += seconds;
		}
	}
}
