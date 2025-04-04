using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Services;

namespace EventStore.Projections.Core.Services.Processing {
	public class EventByTypeIndexEventFilter : EventFilter {
		private readonly HashSet<string> _streams;

		public EventByTypeIndexEventFilter(HashSet<string> events)
			: base(false, false, events) {
			_streams = new HashSet<string>(from eventType in events
										   select "$et-" + eventType);
		}

		protected override bool DeletedNotificationPasses(string positionStreamId) {
			return true;
		}

		public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType) {
			if (_streams.Contains(positionStreamId))
				return true;
			return !resolvedFromLinkTo && !SystemStreams.IsSystemStream(positionStreamId);
		}

		public override string GetCategory(string positionStreamId) {
			return null;
		}
	}
}
