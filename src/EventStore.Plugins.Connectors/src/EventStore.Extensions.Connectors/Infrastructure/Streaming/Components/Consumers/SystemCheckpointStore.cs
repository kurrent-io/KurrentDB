// ReSharper disable CheckNamespace

using System.Text.Json;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Enumerators;

namespace EventStore.Streaming.Consumers.Checkpoints;

/// <summary>
/// This is a temporary solution until we have a proper
/// consumer group coordinator (event sourced).
/// <para />
/// WARNING: only one consumer per group is supported!
/// <para />
/// The coordinator needs to be implemented in the database
/// and provide its own endpoints for the consumer groups.
/// </summary>
public class SystemCheckpointStore : ICheckpointStore {
	public SystemCheckpointStore(IPublisher client, string groupId, string consumerId, int streamMaxSize = 3) {
		Client        = client;
		GroupId       = groupId;
		ConsumerId    = consumerId;
		StreamMaxSize = streamMaxSize;

		CheckpointStreamId = $"$consumer-positions-{groupId}";
	}

	IPublisher       Client                 { get; }
	int              StreamMaxSize          { get; }
	RecordPosition[] LastCommittedPositions { get; set; } = [];

	public string GroupId            { get; }
	public string ConsumerId         { get; }
	public string CheckpointStreamId { get; }
	public bool   Initialized        { get; private set; }

	public async Task Initialize(CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();

		try {
			var (metadata, revision) = await Client.GetStreamMetadata(CheckpointStreamId, cancellationToken);

			if (metadata == StreamMetadata.Empty)
				await Client.SetStreamMetadata(
					CheckpointStreamId, new(maxCount: StreamMaxSize),
					cancellationToken: cancellationToken
				);
			else if (metadata.MaxCount != StreamMaxSize) {
				await Client.SetStreamMetadata(
					CheckpointStreamId, new(maxCount: StreamMaxSize), revision.ToInt64(),
					cancellationToken: cancellationToken
				);
			}

			Initialized = true;
		}
		catch (Exception ex) {
			throw new Exception("Failed to initialize System Checkpoint Store", ex);
		}
	}

	public async Task<RecordPosition[]> GetLatestPositions(CancellationToken cancellationToken = default) {
		if (!Initialized)
			await Initialize(cancellationToken);

		ResolvedEvent? re = null;

		try {
			re = await Client.ReadStreamLastEvent(CheckpointStreamId, cancellationToken);
		}
		catch (ReadResponseException.StreamNotFound) {
			// _log.LogWarning("Checkpoint stream not found. Returning empty positions.");
		}
		catch (Exception ex) {
			throw new Exception("Failed to read latest positions", ex);
		}

		if (re is null)
			return [];

		var positionsCommitted = JsonSerializer.Deserialize<Contracts.PositionsCommitted>(re!.Value.Event.Data.Span);

		return MapToRecordPositions(positionsCommitted!);

		static RecordPosition[] MapToRecordPositions(Contracts.PositionsCommitted positionsCommitted) {
			return positionsCommitted.Positions.Select(
				p => new RecordPosition {
					StreamId       = StreamId.From(p.StreamId),
					StreamRevision = StreamRevision.From(p.StreamRevision),
					LogPosition    = LogPosition.From(
						p.LogPosition.CommitPosition,
						p.LogPosition.PreparePosition
					),
					PartitionId = p.PartitionId
				}
			).ToArray();
		}
	}

	public async Task<RecordPosition[]> CommitPositions(RecordPosition[] positions, CancellationToken cancellationToken = default) {
		Ensure.NotNullOrEmpty(positions);

		if (!Initialized)
			await Initialize(cancellationToken);

		var lastPositionsByStream = positions.GroupBy(x => (x.StreamId, x.PartitionId)).Select(x => x.Last()).ToArray();

		var positionsCommitted = MapToPositionsCommitted(GroupId, ConsumerId, lastPositionsByStream);

		var data = new[] {
			new Event(
				eventId: Guid.NewGuid(),
				eventType: nameof(Contracts.PositionsCommitted),
				isJson: true,
				data: JsonSerializer.SerializeToUtf8Bytes(positionsCommitted),
				metadata: null
			)
		};

		var (position, streamRevision) = await Client
			.WriteEvents(CheckpointStreamId, data, cancellationToken: cancellationToken);

		LastCommittedPositions = lastPositionsByStream;

		return positions;

		static Contracts.PositionsCommitted MapToPositionsCommitted(string groupId, string consumerId, RecordPosition[] recordPositions) {
			var positions = recordPositions.Select(
				rp => new Contracts.RecordPosition(
					rp.StreamId,
					rp.PartitionId,
					new(rp.LogPosition.CommitPosition, rp.LogPosition.PreparePosition),
					rp.StreamRevision
				)
			).ToArray();

			return new(groupId, consumerId, positions, DateTimeOffset.UtcNow);
		}
	}

	public Task DeletePositions(CancellationToken cancellationToken = default) =>
		Client.DeleteStream(CheckpointStreamId, cancellationToken: cancellationToken);

	public Task ResetPositions(CancellationToken cancellationToken = default) =>
		Client.TruncateStream(CheckpointStreamId, cancellationToken);
}

static class Contracts {
	public record LogPosition(
		ulong? CommitPosition,
		ulong? PreparePosition
	);

	public record RecordPosition(
		string StreamId,
		long PartitionId,
		LogPosition LogPosition,
		long StreamRevision
	);

	public record PositionsCommitted(
		string GroupId,
		string ConsumerId,
		RecordPosition[] Positions,
		DateTimeOffset CommittedAt
	);
}

public static class CheckpointStoreExtensions {
	public static async Task<RecordPosition> ResolveStartPosition(
		this ICheckpointStore checkpointStore,
		SubscriptionInitialPosition initialPosition,
		RecordPosition startPosition,
		CancellationToken cancellationToken = default
	) {
		if (startPosition != RecordPosition.Unset)
			return startPosition;

		// currently we only support a single consumer per group and no partitions.
		var positions = await checkpointStore.GetLatestPositions(cancellationToken);

		var subscriptionPosition = positions.Length == 0
			? initialPosition == SubscriptionInitialPosition.Earliest
				? RecordPosition.Unset
				: new() { LogPosition = LogPosition.Latest }
			: positions.Last();

		return subscriptionPosition;
	}
}