using System;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.PersistentSubscription {
	public class PersistentSubscriptionMessageParker : IPersistentSubscriptionMessageParker {
		private readonly IODispatcher _ioDispatcher;
		public readonly string ParkedStreamId;
		private long _lastTruncateBefore = -1;
		private long _lastParkedEventNumber = -1;

		public long ParkedMessageCount {
			get {
				return _lastParkedEventNumber == -1 ? 0 :
					_lastTruncateBefore == -1 ? _lastParkedEventNumber + 1 :
					_lastParkedEventNumber - _lastTruncateBefore + 1;
			}
		}

		private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionMessageParker>();

		public PersistentSubscriptionMessageParker(string subscriptionId, IODispatcher ioDispatcher) {
			ParkedStreamId = "$persistentsubscription-" + subscriptionId + "-parked";
			_ioDispatcher = ioDispatcher;
		}

		public void BeginLoadStats(Action completed) {
			BeginReadParkedMessageStats(completed);
		}

		private Event CreateStreamMetadataEvent(long? tb) {
			var eventId = Guid.NewGuid();
			var acl = new StreamAcl(
				readRole: SystemRoles.Admins, writeRole: SystemRoles.Admins,
				deleteRole: SystemRoles.Admins, metaReadRole: SystemRoles.Admins,
				metaWriteRole: SystemRoles.Admins);
			var metadata = new StreamMetadata(cacheControl: null,
				truncateBefore: tb,
				acl: acl);
			var dataBytes = metadata.ToJsonBytes();
			return new Event(eventId, SystemEventTypes.StreamMetadata, isJson: true, data: dataBytes, metadata: null);
		}

		private void WriteStateCompleted(Action<ResolvedEvent, OperationResult> completed, ResolvedEvent ev,
			ClientMessage.WriteEventsCompleted msg) {
			_lastParkedEventNumber = msg.LastEventNumber;
			completed?.Invoke(ev, msg.Result);
		}

		public void BeginParkMessage(ResolvedEvent ev, string reason,
			Action<ResolvedEvent, OperationResult> completed) {
			var metadata = new ParkedMessageMetadata { Added = DateTime.Now, Reason = reason, SubscriptionEventNumber = ev.OriginalEventNumber };

			string data = GetLinkToFor(ev);

			var parkedEvent = new Event(Guid.NewGuid(), SystemEventTypes.LinkTo, false, data, metadata.ToJson());

			_ioDispatcher.WriteEvent(ParkedStreamId, ExpectedVersion.Any, parkedEvent, SystemAccounts.System,
				x => WriteStateCompleted(completed, ev, x));
		}

		private string GetLinkToFor(ResolvedEvent ev) {
			if (ev.Event == null) // Unresolved link so just use the bad/deleted link data.
			{
				return Encoding.UTF8.GetString(ev.Link.Data.Span);
			}

			return string.Format("{0}@{1}", ev.Event.EventNumber, ev.Event.EventStreamId);
		}

		public void BeginDelete(Action<IPersistentSubscriptionMessageParker> completed) {
			_ioDispatcher.DeleteStream(ParkedStreamId, ExpectedVersion.Any, false, SystemAccounts.System,
				x => completed?.Invoke(this));
		}

		private void BeginReadParkedMessageStats(Action completed) {
			BeginReadLastEvent(lastEventNumber => {
				if (lastEventNumber is null)
					completed();
				BeginReadFirstEvent(0, firstEventNumber => {
					_lastTruncateBefore = firstEventNumber ?? -1;
					_lastParkedEventNumber = lastEventNumber ?? -1;
					completed?.Invoke();
				});
			});
		}

		public void BeginReadEndSequence(Action<long?> completed) {
			_ioDispatcher.ReadBackward(ParkedStreamId,
				long.MaxValue,
				1,
				false,
				SystemAccounts.System, comp => {
					switch (comp.Result) {
						case ReadStreamResult.Success:
							completed?.Invoke(comp.LastEventNumber);
							break;
						case ReadStreamResult.NoStream:
							completed?.Invoke(null);
							break;
						default:
							Log.Error(
								"An error occured reading the last event in the parked message stream {stream} due to {e}.",
								ParkedStreamId, comp.Result);
							Log.Error("Messages were not removed on retry");
							break;
					}
				});
		}

		private void BeginReadFirstEvent(long fromEventNumber, Action<long?> completed) {
			_ioDispatcher.ReadForward(
				ParkedStreamId,
				fromEventNumber,
				1, false, SystemAccounts.System,
				comp => {
					switch (comp.Result) {
						case ReadStreamResult.Success:
							if (comp.Events.Any()) {
								completed?.Invoke(comp.Events.First().OriginalEventNumber);
							} else if (!comp.IsEndOfStream) {
								BeginReadFirstEvent(comp.NextEventNumber, completed);
							} else {
								completed?.Invoke(null);
							}

							break;
						case ReadStreamResult.NoStream:
						case ReadStreamResult.StreamDeleted:
							completed?.Invoke(null);
							break;
						default:
							Log.Error(
								$"An error occured reading the first event in the parked message stream {ParkedStreamId} due to {comp.Result}.");
							completed?.Invoke(null);
							break;
					}
				}, () => {
					Log.Error(
						$"Timed out reading the first event in the parked message stream {ParkedStreamId}. Parked message stats may be incorrect.");
					completed?.Invoke(null);
				}, Guid.NewGuid());
		}

		private void BeginReadLastEvent(Action<long?> completed) {
			_ioDispatcher.ReadBackward(
				ParkedStreamId,
				-1,
				1,
				false,
				SystemAccounts.System,
				comp => {
					switch (comp.Result) {
						case ReadStreamResult.Success:
							if (comp.Events.Any()) {
								completed?.Invoke(comp.Events.Last().OriginalEventNumber);
							} else {
								completed?.Invoke(null);
							}

							break;
						case ReadStreamResult.NoStream:
						case ReadStreamResult.StreamDeleted:
							completed?.Invoke(null);
							break;
						default:
							Log.Error(
								$"An error occured reading the last event in the parked message stream {ParkedStreamId} due to {comp.Result}.");
							completed?.Invoke(null);
							break;
					}
				}, () => {
					Log.Error(
						$"Timed out reading the last event in the parked message stream {ParkedStreamId}. Parked message stats may be incorrect.");
					completed?.Invoke(null);
				}, Guid.NewGuid());
		}

		public void BeginMarkParkedMessagesReprocessed(long sequence) {
			BeginMarkParkedMessagesReprocessed(sequence, null);
		}

		public void BeginMarkParkedMessagesReprocessed(long sequence, Action completed) {
			var metaStreamId = SystemStreams.MetastreamOf(ParkedStreamId);
			_ioDispatcher.WriteEvent(
				metaStreamId, ExpectedVersion.Any, CreateStreamMetadataEvent(sequence), SystemAccounts.System,
				msg => {
					switch (msg.Result) {
						case OperationResult.Success:
							_lastTruncateBefore = sequence;
							completed?.Invoke();
							break;
						default:
							Log.Error("An error occured truncating the parked message stream {stream} due to {e}.",
								ParkedStreamId, msg.Result);
							Log.Error("Messages were not removed on retry");
							completed?.Invoke();
							break;
					}
				});
		}

		class ParkedMessageMetadata {
			public DateTime Added { get; set; }
			public string Reason { get; set; }
			public long SubscriptionEventNumber { get; set; }
		}
	}
}
