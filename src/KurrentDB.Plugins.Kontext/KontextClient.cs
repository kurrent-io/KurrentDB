using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kurrent.Kontext;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using Polly;

namespace KurrentDB.Plugins.Kontext;

/// <summary>
/// Implements <see cref="IKontextClient"/> using KurrentDB's internal <see cref="ISystemClient"/>.
/// </summary>
public class KontextClient(ISystemClient systemClient) : IKontextClient {
	public async IAsyncEnumerable<KontextEvent> SubscribeToAll(
		(ulong Commit, ulong Prepare)? checkpoint,
		Action? onCaughtUp,
		[EnumeratorCancellation] CancellationToken ct) {

		var startPosition = checkpoint.HasValue
			? new Position(checkpoint.Value.Commit, checkpoint.Value.Prepare)
			: (Position?)null;

		var channel = Channel.CreateBounded<ReadResponse>(new BoundedChannelOptions(64) {
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait,
		});

		await systemClient.Subscriptions.SubscribeToAll(
			startPosition, EventFilter.Unfiltered, maxSearchWindow: 32,
			channel, ResiliencePipeline.Empty, ct);

		var isCaughtUp = false;
		await foreach (var response in channel.Reader.ReadAllAsync(ct)) {
			if (response is ReadResponse.EventReceived { Event: var resolved }) {
				var record = resolved.OriginalEvent;
				var position = resolved.OriginalPosition;
				yield return new KontextEvent(
					record.EventStreamId,
					(ulong)record.EventNumber,
					record.EventType,
					(ulong)(position?.CommitPosition ?? record.LogPosition),
					(ulong)(position?.PreparePosition ?? record.LogPosition),
					record.TimeStamp,
					record.Data,
					record.Metadata);
			} else if (!isCaughtUp && response is ReadResponse.SubscriptionCaughtUp) {
				isCaughtUp = true;
				onCaughtUp?.Invoke();
			}
		}
	}

	public async Task<ulong?> GetHeadPositionAsync(CancellationToken ct = default) {
		var lastPosition = await systemClient.GetLastPosition(ct);
		return lastPosition >= 0 ? (ulong)lastPosition : null;
	}

	public async IAsyncEnumerable<EventResult> ReadAsync(string stream, long eventNumber, long? to = null) {
		var limit = to.HasValue ? (to.Value - eventNumber + 1) : 1;

		IAsyncEnumerable<ResolvedEvent> source;
		try {
			source = systemClient.Reading.ReadStreamForwards(stream, StreamRevision.FromInt64(eventNumber), limit);
		} catch (ReadResponseException.StreamNotFound) {
			yield break;
		}

		IAsyncEnumerator<ResolvedEvent>? enumerator = null;
		try {
			enumerator = source.GetAsyncEnumerator();
			while (true) {
				bool hasNext;
				try {
					hasNext = await enumerator.MoveNextAsync();
				} catch (ReadResponseException.StreamNotFound) {
					yield break;
				}
				if (!hasNext) break;

				var resolved = enumerator.Current;
				var record = resolved.OriginalEvent;
				yield return new EventResult {
					Stream = stream,
					EventNumber = record.EventNumber,
					CommitPosition = resolved.OriginalPosition?.CommitPosition,
					EventType = record.EventType,
					Timestamp = record.TimeStamp,
					Data = EventResultHelpers.TryParseJson(record.Data),
					Metadata = EventResultHelpers.TryParseJson(record.Metadata),
				};
			}
		} finally {
			if (enumerator != null)
				await enumerator.DisposeAsync();
		}
	}

	public async Task WriteAsync(string stream, string eventType, ReadOnlyMemory<byte> data) {
		var evt = new Event(Guid.NewGuid(), eventType, isJson: true, data.ToArray());
		await systemClient.Writing.WriteEvents(stream, [evt]);
	}

	public async Task WriteBatchAsync(IReadOnlyList<(string Stream, string EventType, ReadOnlyMemory<byte> Data)> events) {
		foreach (var group in events.GroupBy(e => e.Stream)) {
			var streamEvents = group.Select(e =>
				new Event(Guid.NewGuid(), e.EventType, isJson: true, e.Data.ToArray())).ToArray();
			await systemClient.Writing.WriteEvents(group.Key, streamEvents);
		}
	}
}
