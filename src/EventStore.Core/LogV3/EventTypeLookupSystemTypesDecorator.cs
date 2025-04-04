using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV3 {
	public class EventTypeLookupSystemTypesDecorator : INameLookup<uint> {
		private readonly INameLookup<uint> _wrapped;

		public EventTypeLookupSystemTypesDecorator(INameLookup<uint> wrapped) {
			_wrapped = wrapped;
		}

		public bool TryGetName(uint eventTypeId, out string name) {
			if (LogV3SystemEventTypes.TryGetVirtualEventType(eventTypeId, out name))
				return true;

			return _wrapped.TryGetName(eventTypeId, out name);
		}

		public bool TryGetLastValue(out uint last) {
			return _wrapped.TryGetLastValue(out last);
		}
	}
}
