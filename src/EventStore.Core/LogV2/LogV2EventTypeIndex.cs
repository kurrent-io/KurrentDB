using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.LogV2 {
	public class LogV2EventTypeIndex :
		INameIndex<string>,
		INameIndexConfirmer<string>,
		IValueLookup<string>,
		INameLookup<string> {

		public void Dispose() {
		}

		public void InitializeWithConfirmed(INameLookup<string> source) {
		}

		public void CancelReservations() {
		}

		public void Confirm(IList<IPrepareLogRecord<string>> prepares, bool catchingUp, IIndexBackend<string> backend) {
		}

		public void Confirm(
			IList<IPrepareLogRecord<string>> prepares,
			CommitLogRecord commit,
			bool catchingUp,
			IIndexBackend<string> backend) {
		}

		public bool GetOrReserve(string eventType, out string eventTypeId, out string createdId, out string createdName) {
			Ensure.NotNull(eventType, "eventType");
			eventTypeId = eventType;
			createdId = default;
			createdName = default;

			return true;
		}

		public string LookupValue(string eventTypeName) => eventTypeName;

		public bool TryGetName(string eventTypeId, out string name) {
			name = eventTypeId;
			return true;
		}

		public bool TryGetLastValue(out string last) {
			throw new System.NotImplementedException();
		}
	}
}
