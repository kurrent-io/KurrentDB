using System.Threading.Channels;

namespace EventStore.Streaming.Consumers.Checkpoints;

/// <summary>
/// Keeps track of the latest processed record positions in-memory and commits them to the server at a given interval or by request.
/// Checkpoints store how far along a consumer is in the streams it is processing from.
/// </summary>
public class CheckpointController : ICheckpointController {
	public CheckpointController(CheckpointControllerOptions options) {
		Options          = options;
		CommitPositions  = options.CommitPositions;
		Logger           = options.LoggerFactory.CreateLogger(nameof(CheckpointController));
		TrackedPositions = [];
		CommitInProgress = new(initialValue: false);

		Timer = new(
			options.AutoCommit.Interval,
			(isLastTick, ct) => {
				if (isLastTick)
					Logger.LogTrace("auto commit timer last tick");

				return Commit(ct);
			}
		);
		
		TrackedRecords = Channel.CreateUnbounded<EventStoreRecord>(
			new UnboundedChannelOptions {
				SingleReader = true,
				SingleWriter = true
			}
		);
	}

	CheckpointControllerOptions                     Options          { get; }
	Func<RecordPosition[], CancellationToken, Task> CommitPositions  { get; }
	ILogger                                         Logger           { get; }
	AsyncPeriodicTimer                              Timer            { get; }
	Channel<EventStoreRecord>                       TrackedRecords   { get; }
	InterlockedBoolean                              CommitInProgress { get; }
	TrackedPositionSet                              TrackedPositions { get; }

	bool                    Active      { get; set; }
	CancellationTokenSource Cancellator { get; set; } = null!;
	
	public IEnumerable<RecordPosition> Positions => TrackedPositions.Select(x => x.Position);
	
	/// <inheritdoc />
	public Task Activate(CancellationToken stoppingToken = default) {
		if (Active)
			return Task.CompletedTask;
		
		Cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		
		if (Options.AutoCommit.AutoCommitEnabled) {
			Timer.Start(Cancellator.Token);
			Logger.LogTrace("auto commit timer activated");

			_ = Task.Run(
				async () => {
					await foreach (var record in TrackedRecords.Reader.ReadAllAsync(CancellationToken.None)) {
						TrackedPositions.Add(record);

						if (!CommitInProgress.CurrentValue 
						 && TrackedPositions.NextReadyPosition != TrackedPosition.None
						 && TrackedPositions.Count >= Options.AutoCommit.RecordsThreshold)
							await Commit(CancellationToken.None);
					}
				}
			);
		}
		else {
			_ = Task.Run(
				async () => {
					await foreach (var record in TrackedRecords.Reader.ReadAllAsync(CancellationToken.None))
						TrackedPositions.Add(record);
				}
			);
		}
		
		Active = true;
		
		return Task.CompletedTask;
	}
	
	/// <inheritdoc />
	public async Task Track(EventStoreRecord record) {
		if (record.Position == RecordPosition.Unset)
			return;

		await TrackedRecords.Writer.WriteAsync(record);
	}
	
	/// <inheritdoc />
	public async Task<RecordPosition[]> Commit(CancellationToken cancellationToken = default) {
		var trackedPositions = TrackedPositions.Clone();
		
		if (CommitInProgress.EnsureCalledOnce())
			return [];
		
		try {
			if (trackedPositions.NextReadyPosition != TrackedPosition.None) {
				var readyPositions = trackedPositions
					.TakeWhile(x => x.SequenceId <= trackedPositions.NextReadyPosition.SequenceId)
					.Select(x => x.Position)
					.ToArray();
				
				await CommitPositions(readyPositions, cancellationToken);
				
				TrackedPositions.Flush();

				return readyPositions;
			}
			else {
				await CommitPositions([], cancellationToken);
			}
		}
		finally {
			CommitInProgress.Set(false);
		}
		
		return [];
	}
	
	/// <inheritdoc />
	public async ValueTask Deactivate() {
		if (!Active)
			return;
		
		Active = false;

		await TrackedRecords.Complete();

		if (!Cancellator.IsCancellationRequested && Options.AutoCommit.AutoCommitEnabled) {
			Logger.LogTrace("canceling auto commit timer...");
			await Cancellator.CancelAsync(); // this will stop the timer
			Logger.LogTrace("disposing auto commit timer...");
			Cancellator.Dispose();
			Logger.LogTrace("auto commit timer deactivated");
		}
	}

	public ValueTask DisposeAsync() => Deactivate();
}