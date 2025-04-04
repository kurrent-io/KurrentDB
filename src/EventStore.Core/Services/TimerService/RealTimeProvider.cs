using System;

namespace EventStore.Core.Services.TimerService {
	public class RealTimeProvider : ITimeProvider {
		public DateTime UtcNow {
			get { return DateTime.UtcNow; }
		}

		public DateTime LocalTime {
			get { return DateTime.Now; }
		}
	}
}
