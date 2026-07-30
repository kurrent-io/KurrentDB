// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.PersistentSubscription;

public class PersistentSubscriptionMessageParker : IPersistentSubscriptionMessageParker {
	private readonly IODispatcher _ioDispatcher;
	public readonly string ParkedStreamId;
	private long _lastTruncateBefore = 0;
	private long _lastParkedEventNumber = -1;
	private DateTime? _oldestParkedMessage;
	private long _parkedDueToClientNak;
	private long _parkedDueToMaxRetries;
	private long _parkedMessageReplays;
	private long _parkedMessageTruncations;

	public long ParkedMessageCount =>
		_lastParkedEventNumber == -1
			? 0
			: _lastParkedEventNumber - _lastTruncateBefore + 1;

	public long ParkedDueToClientNak => Interlocked.Read(ref _parkedDueToClientNak);
	public long ParkedDueToMaxRetries => Interlocked.Read(ref _parkedDueToMaxRetries);
	public long ParkedMessageReplays => Interlocked.Read(ref _parkedMessageReplays);
	public long ParkedMessageTruncations => Interlocked.Read(ref _parkedMessageTruncations);

	// The stats reads are best-effort and only feed ParkedMessageCount/oldest-timestamp. A timeout that
	// fires would produce degraded stats, so we use a long one it should never hit in practice, while
	// still guaranteeing the read completes (rather than stalling) if a response is ever truly lost.
	private static readonly TimeSpan StatsReadTimeout = TimeSpan.FromMinutes(5);

	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionMessageParker>();

	public PersistentSubscriptionMessageParker(string subscriptionId, IODispatcher ioDispatcher) {
		ParkedStreamId = "$persistentsubscription-" + subscriptionId + "-parked";
		_ioDispatcher = ioDispatcher;
	}

	public DateTime? GetOldestParkedMessage {
		get {
			return _oldestParkedMessage;
		}
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
		return new Event(eventId, SystemEventTypes.StreamMetadata, isJson: true, data: dataBytes);
	}

	private void WriteStateCompleted(Action<ResolvedEvent, OperationResult> completed, ResolvedEvent ev,
		ClientMessage.WriteEventsCompleted msg, DateTime parkedMessageAdded) {

		_lastParkedEventNumber = msg.LastEventNumbers.Single;
		if (_oldestParkedMessage == null)
			_oldestParkedMessage = parkedMessageAdded.ToUniversalTime();
		completed?.Invoke(ev, msg.Result);
	}

	public void BeginParkMessage(ResolvedEvent ev, string reason, ParkReason parkReason,
		Action<ResolvedEvent, OperationResult> completed) {
		switch (parkReason) {
			case ParkReason.MaxRetries:
				Interlocked.Increment(ref _parkedDueToMaxRetries);
				break;
			case ParkReason.ClientNak:
				Interlocked.Increment(ref _parkedDueToClientNak);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(parkReason));
		}

		var metadata = new ParkedMessageMetadata { Added = DateTime.Now, Reason = reason, SubscriptionEventNumber = ev.OriginalEventNumber };

		string data = GetLinkToFor(ev);

		var parkedEvent = new Event(Guid.NewGuid(), SystemEventTypes.LinkTo, false, data, metadata.ToJson());

		_ioDispatcher.WriteEvent(ParkedStreamId, ExpectedVersion.Any, parkedEvent, SystemAccounts.System,
			x => WriteStateCompleted(completed, ev, x,
				metadata.Added));
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
		BeginReadTruncateBefore(truncateBefore => {
			BeginReadLastEvent(lastEventNumber => {
				if (lastEventNumber is null) {
					// parked stream is empty
					_lastTruncateBefore = truncateBefore ?? 0;
					_lastParkedEventNumber = -1;
					_oldestParkedMessage = null;
					completed?.Invoke();
					return;
				}

				BeginReadFirstEvent(0, (firstEventNumber, oldestParkedMessageTimeStamp) => {
					_lastTruncateBefore = firstEventNumber ?? truncateBefore ?? 0;
					_lastParkedEventNumber = lastEventNumber ?? -1;
					_oldestParkedMessage = oldestParkedMessageTimeStamp;
					completed?.Invoke();
				});
			});
		});
	}

	// for parked message stats
	private void BeginReadTruncateBefore(Action<long?> completed) {
		_ioDispatcher.ReadBackward(
			streamId: SystemStreams.MetastreamOf(ParkedStreamId),
			fromEventNumber: -1,
			maxCount: 1,
			resolveLinks: false,
			principal: SystemAccounts.System,
			action: comp => {
				switch (comp.Result) {
					case ReadStreamResult.Success when comp.Events.Any():
						var metadata = StreamMetadata.FromJsonBytes(comp.Events[0].Event.Data);
						completed?.Invoke(metadata.TruncateBefore);
						break;
					default:
						completed?.Invoke(null);
						break;
				}
			},
			timeoutAction: () => {
				Log.Error($"Timed out reading truncate-before for the parked message stream {ParkedStreamId}. Parked message stats may be incorrect.");
				completed?.Invoke(null);
			},
			corrId: Guid.NewGuid(),
			timeout: StatsReadTimeout);
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
					case ReadStreamResult.StreamDeleted:
						completed?.Invoke(null);
						break;
					default:
						Log.Error(
							"An error occured reading the last event in the parked message stream {stream} due to {e}.",
							ParkedStreamId, comp.Result);
						// Treat an unexpected read error as "nothing to do" rather than leaving the caller
						// hanging: the operation is abandoned (logged above) and its mutual-exclusion flag
						// released, instead of wedging all future replays/truncates on this subscription.
						completed?.Invoke(null);
						break;
				}
			},
			expires: ClientMessage.ReadRequestMessage.NeverExpires);
	}

	// for parked message stats
	private void BeginReadFirstEvent(long fromEventNumber, Action<long?, DateTime?> completed) {
		_ioDispatcher.ReadForward(
			ParkedStreamId,
			fromEventNumber,
			1, false, SystemAccounts.System,
			comp => {
				switch (comp.Result) {
					case ReadStreamResult.Success:
						if (comp.Events.Any()) {
							completed?.Invoke(comp.Events.First().OriginalEventNumber, comp.Events.First().OriginalEvent.TimeStamp);
						} else if (!comp.IsEndOfStream) {
							BeginReadFirstEvent(comp.NextEventNumber, completed);
						} else {
							completed?.Invoke(null, null);
						}

						break;
					case ReadStreamResult.NoStream:
					case ReadStreamResult.StreamDeleted:
						completed?.Invoke(null, null);
						break;
					default:
						Log.Error(
							$"An error occured reading the first event in the parked message stream {ParkedStreamId} due to {comp.Result}.");
						completed?.Invoke(null, null);
						break;
				}
			},
			timeoutAction: () => {
				Log.Error(
					$"Timed out reading the first event in the parked message stream {ParkedStreamId}. Parked message stats may be incorrect.");
				completed?.Invoke(null, null);
			},
			corrId: Guid.NewGuid(),
			timeout: StatsReadTimeout);
	}

	// for parked message stats
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
			},
			timeoutAction: () => {
				Log.Error(
					$"Timed out reading the last event in the parked message stream {ParkedStreamId}. Parked message stats may be incorrect.");
				completed?.Invoke(null);
			},
			corrId: Guid.NewGuid(),
			timeout: StatsReadTimeout);
	}

	// updates the truncateBefore for the parked stream
	public void BeginMarkParkedMessagesReprocessed(long sequence, Action completed = null) {
		var metaStreamId = SystemStreams.MetastreamOf(ParkedStreamId);
		// The truncate-before only ever advances, so replaying/truncating to an already-passed point
		// never resurrects previously removed parked messages.
		var truncateBefore = Math.Max(_lastTruncateBefore, sequence);
		if (truncateBefore == _lastTruncateBefore) {
			// nothing would change: skip the redundant metastream write (and the stats reload it triggers).
			completed?.Invoke();
			return;
		}

		_ioDispatcher.WriteEvent(
			metaStreamId, ExpectedVersion.Any, CreateStreamMetadataEvent(truncateBefore), SystemAccounts.System,
			// this callback is guaranteed to be called (writes get replies on timeout)
			msg => {
				switch (msg.Result) {
					case OperationResult.Success:
						break;
					default:
						Log.Error("An error occured truncating the parked message stream {stream} due to {e}.",
							ParkedStreamId, msg.Result);
						Log.Error("Messages were not removed on retry");
						break;
				}

				// we've updated the tb, update the first/last parked message numbers/timestamps
				BeginLoadStats(completed ?? Empty.Action);
			});
	}

	public void NotifyReplay() {
		Interlocked.Increment(ref _parkedMessageReplays);
	}

	public void NotifyTruncate() {
		Interlocked.Increment(ref _parkedMessageTruncations);
	}

	class ParkedMessageMetadata {
		public DateTime Added { get; set; }
		public string Reason { get; set; }
		public long SubscriptionEventNumber { get; set; }
	}
}
