# Projections v2 Engine — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a new projections processing engine that uses filtered read-all enumerators, DuckDB secondary indexes, partitioned parallel processing, and multi-stream atomic appends.

**Architecture:** Events are read via one of three strategies (filtered $all, direct stream, DuckDB stream-set), dispatched to N partition channels by hash of partition key, processed by the existing Jint runtime, and checkpointed atomically via Chandy-Lamport markers and multi-stream append.

**Tech Stack:** C# / .NET 10.0, System.Threading.Channels, TUnit (testing), existing KurrentDB infrastructure (enumerators, ClientMessage.WriteEvents, IProjectionStateHandler)

**Design Doc:** `docs/plans/2026-03-07-projections-v2-engine-design.md`

---

## Task 1: Project Setup and Skeleton

Create the new engine's project structure within the existing projections project. The v2 engine lives in `src/KurrentDB.Projections.Core/` under a new `V2/` namespace.

**Files:**
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/ProjectionEngineV2.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/ProjectionEngineV2Config.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/IReadStrategy.cs`

### Step 1: Create the engine config

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/ProjectionEngineV2Config.cs
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2Config {
	public required string ProjectionName { get; init; }
	public required IQuerySources SourceDefinition { get; init; }
	public required IProjectionStateHandler StateHandler { get; init; }
	public int PartitionCount { get; init; } = 4;
	public int CheckpointAfterMs { get; init; } = 2000;
	public int CheckpointHandledThreshold { get; init; } = 4000;
	public long CheckpointUnhandledBytesThreshold { get; init; } = 10_000_000;
	public bool EmitEnabled { get; init; }
	public bool TrackEmittedStreams { get; init; }
}
```

### Step 2: Create the read strategy interface

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/IReadStrategy.cs
using System.Collections.Generic;
using System.Threading;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// Produces events in log-position order from a specific source.
/// </summary>
public interface IReadStrategy : IAsyncDisposable {
	IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, CancellationToken ct);
}
```

### Step 3: Create the engine skeleton

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/ProjectionEngineV2.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2 : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config;
	private readonly IReadStrategy _readStrategy;
	private CancellationTokenSource _cts;
	private Task _runTask;

	public ProjectionEngineV2(ProjectionEngineV2Config config, IReadStrategy readStrategy) {
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
	}

	public Task Start(TFPos checkpoint, CancellationToken ct) {
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_runTask = Run(checkpoint, _cts.Token);
		return Task.CompletedTask;
	}

	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint}", _config.ProjectionName, checkpoint);
		// Pipeline will be built in subsequent tasks
		throw new NotImplementedException();
	}

	public async ValueTask DisposeAsync() {
		if (_cts is not null) {
			await _cts.CancelAsync();
			if (_runTask is not null) {
				try { await _runTask; } catch (OperationCanceledException) { }
			}
			_cts.Dispose();
		}
		await _readStrategy.DisposeAsync();
	}
}
```

### Step 4: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/
git commit -m "feat(projections-v2): add engine skeleton with config and read strategy interface"
```

---

## Task 2: Stream-Name-Set Event Filter

Add a new `IEventFilter` implementation that filters events by a set of stream names. This is needed for `fromStreams()` support.

**Files:**
- Modify: `src/KurrentDB.Core/Services/Storage/ReaderIndex/EventFilter.cs:25-39` (add factory method)
- Test: `src/KurrentDB.Core.Tests/Services/Storage/ReaderIndex/EventFilterTests.cs` (new file or find existing)

### Step 1: Check for existing filter tests

Run: `find src/KurrentDB.Core.Tests -name "*EventFilter*" -o -name "*event_filter*" | head -20`

### Step 2: Write the failing test

Create or extend the test file:

```csharp
// In the appropriate test file
[Fact]
public void stream_name_set_filter_allows_matching_stream() {
	var filter = EventFilter.StreamName.Set(isAllStream: false, "stream-a", "stream-b");
	var record = CreateEventRecord("stream-a", "SomeEvent");
	Assert.True(filter.IsEventAllowed(record));
}

[Fact]
public void stream_name_set_filter_rejects_non_matching_stream() {
	var filter = EventFilter.StreamName.Set(isAllStream: false, "stream-a", "stream-b");
	var record = CreateEventRecord("stream-c", "SomeEvent");
	Assert.False(filter.IsEventAllowed(record));
}

[Fact]
public void stream_name_set_filter_with_all_stream_rejects_system_streams() {
	var filter = EventFilter.StreamName.Set(isAllStream: true, "$system-stream", "user-stream");
	var systemRecord = CreateEventRecord("$epoch-information", "SomeEvent");
	Assert.False(filter.IsEventAllowed(systemRecord));
}
```

### Step 3: Run tests to verify they fail

Run: `dotnet test src/KurrentDB.Core.Tests/ --filter "stream_name_set_filter" -v n`
Expected: FAIL — `StreamName.Set` method does not exist

### Step 4: Implement the filter

Edit `src/KurrentDB.Core/Services/Storage/ReaderIndex/EventFilter.cs`.

Add inside `public static class StreamName` (after line 30):

```csharp
public static IEventFilter Set(bool isAllStream, params string[] streamNames)
	=> new StreamIdSetStrategy(isAllStream, streamNames);
```

Add the strategy class after `StreamIdRegexStrategy` (after line 177):

```csharp
private sealed class StreamIdSetStrategy : IEventFilter {
	internal readonly bool _isAllStream;
	internal readonly HashSet<string> _streamNames;

	public StreamIdSetStrategy(bool isAllStream, string[] streamNames) {
		_isAllStream = isAllStream;
		_streamNames = new HashSet<string>(streamNames, StringComparer.Ordinal);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	public bool IsEventAllowed(EventRecord eventRecord) =>
		(!_isAllStream || DefaultAllFilter.IsEventAllowed(eventRecord)) &&
		_streamNames.Contains(eventRecord.EventStreamId);

	public override string ToString() =>
		$"{nameof(StreamIdSetStrategy)}: ({string.Join(", ", _streamNames)})";
}
```

Update `ParseToDto` (after line 215) to handle the new type:

```csharp
case StreamIdSetStrategy siss:
	return new() {
		Context = StreamIdContext,
		Type = "set",
		Data = string.Join(",", siss._streamNames),
		IsAllStream = siss._isAllStream
	};
```

### Step 5: Run tests to verify they pass

Run: `dotnet test src/KurrentDB.Core.Tests/ --filter "stream_name_set_filter" -v n`
Expected: PASS

### Step 6: Commit

```bash
git add src/KurrentDB.Core/Services/Storage/ReaderIndex/EventFilter.cs
git add src/KurrentDB.Core.Tests/
git commit -m "feat: add StreamName.Set event filter for stream-name-set matching"
```

---

## Task 3: Read Strategies — Filtered All and Stream Set

Implement the two main read strategies that wrap existing enumerators.

**Files:**
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/FilteredAllReadStrategy.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/StreamSetReadStrategy.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/ReadStrategyFactory.cs`

### Step 1: Implement FilteredAllReadStrategy

This wraps `Enumerator.AllSubscriptionFiltered` (defined in `src/KurrentDB.Core/Services/Transport/Enumerators/Enumerator.AllSubscriptionFiltered.cs:23`).

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/FilteredAllReadStrategy.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public class FilteredAllReadStrategy : IReadStrategy {
	private readonly IPublisher _bus;
	private readonly IEventFilter _eventFilter;
	private readonly ClaimsPrincipal _user;
	private readonly bool _requiresLeader;
	private readonly uint _checkpointIntervalMultiplier;
	private Enumerator.AllSubscriptionFiltered _enumerator;

	public FilteredAllReadStrategy(
		IPublisher bus,
		IEventFilter eventFilter,
		ClaimsPrincipal user,
		bool requiresLeader = false,
		uint checkpointIntervalMultiplier = 1) {
		_bus = bus;
		_eventFilter = eventFilter;
		_user = user;
		_requiresLeader = requiresLeader;
		_checkpointIntervalMultiplier = checkpointIntervalMultiplier;
	}

	public async IAsyncEnumerable<ReadResponse> ReadFrom(
		TFPos checkpoint,
		[EnumeratorCancellation] CancellationToken ct) {

		var position = checkpoint == TFPos.HeadOfTf
			? (Position?)null
			: Position.FromInt64(checkpoint.CommitPosition, checkpoint.PreparePosition);

		_enumerator = new Enumerator.AllSubscriptionFiltered(
			bus: _bus,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: position,
			resolveLinks: true,
			eventFilter: _eventFilter,
			user: _user,
			requiresLeader: _requiresLeader,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: _checkpointIntervalMultiplier,
			cancellationToken: ct);

		while (await _enumerator.MoveNextAsync()) {
			yield return _enumerator.Current;
		}
	}

	public async ValueTask DisposeAsync() {
		if (_enumerator is not null)
			await _enumerator.DisposeAsync();
	}
}
```

### Step 2: Implement StreamSetReadStrategy

This wraps `Enumerator.AllSubscriptionFiltered` with the new `StreamName.Set` filter from Task 2.

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/StreamSetReadStrategy.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public class StreamSetReadStrategy : IReadStrategy {
	private readonly IPublisher _bus;
	private readonly string[] _streamNames;
	private readonly ClaimsPrincipal _user;
	private readonly bool _requiresLeader;
	private Enumerator.AllSubscriptionFiltered _enumerator;

	public StreamSetReadStrategy(
		IPublisher bus,
		string[] streamNames,
		ClaimsPrincipal user,
		bool requiresLeader = false) {
		_bus = bus;
		_streamNames = streamNames;
		_user = user;
		_requiresLeader = requiresLeader;
	}

	public async IAsyncEnumerable<ReadResponse> ReadFrom(
		TFPos checkpoint,
		[EnumeratorCancellation] CancellationToken ct) {

		var position = checkpoint == TFPos.HeadOfTf
			? (Position?)null
			: Position.FromInt64(checkpoint.CommitPosition, checkpoint.PreparePosition);

		var filter = EventFilter.StreamName.Set(isAllStream: true, _streamNames);

		_enumerator = new Enumerator.AllSubscriptionFiltered(
			bus: _bus,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: position,
			resolveLinks: true,
			eventFilter: filter,
			user: _user,
			requiresLeader: _requiresLeader,
			maxSearchWindow: null,
			checkpointIntervalMultiplier: 1,
			cancellationToken: ct);

		while (await _enumerator.MoveNextAsync()) {
			yield return _enumerator.Current;
		}
	}

	public async ValueTask DisposeAsync() {
		if (_enumerator is not null)
			await _enumerator.DisposeAsync();
	}
}
```

### Step 3: Implement ReadStrategyFactory

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/ReadStrategyFactory.cs
using System;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public static class ReadStrategyFactory {
	public static IReadStrategy Create(IQuerySources sources, IPublisher bus, ClaimsPrincipal user) {
		if (sources.AllStreams) {
			var filter = BuildFilterForAllStreams(sources);
			return new FilteredAllReadStrategy(bus, filter, user);
		}

		if (sources.HasStreams()) {
			if (sources.Streams.Length == 1) {
				// Single stream: use direct stream read (future enhancement).
				// For now, fall back to stream-set with one stream.
				return new StreamSetReadStrategy(bus, sources.Streams, user);
			}
			return new StreamSetReadStrategy(bus, sources.Streams, user);
		}

		if (sources.HasCategories()) {
			// fromCategory: filter by stream name prefix "categoryName-"
			var prefixes = sources.Categories.Select(c => c + "-").ToArray();
			var filter = EventFilter.StreamName.Prefixes(isAllStream: true, prefixes);
			return new FilteredAllReadStrategy(bus, filter, user);
		}

		throw new ArgumentException("Unsupported source definition: must specify allStreams, streams, or categories");
	}

	private static IEventFilter BuildFilterForAllStreams(IQuerySources sources) {
		if (sources.HasEvents()) {
			// fromAll().when({EventType: ...}) — filter by event types
			return EventFilter.EventType.Prefixes(isAllStream: true, sources.Events);
		}
		return EventFilter.DefaultAllFilter;
	}
}
```

### Step 4: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/ReadStrategies/
git commit -m "feat(projections-v2): add read strategies (filtered all, stream set, factory)"
```

---

## Task 4: Partition Dispatcher and Channels

Build the dispatcher that routes events to N partition channels by hash of partition key.

**Files:**
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionDispatcher.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionEvent.cs`

### Step 1: Define the partition event type

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionEvent.cs
using KurrentDB.Core.Data;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// An event routed to a specific partition, or a checkpoint marker.
/// </summary>
public readonly record struct PartitionEvent {
	/// <summary>Regular event dispatched to this partition.</summary>
	public ResolvedEvent? Event { get; init; }

	/// <summary>The partition key for this event.</summary>
	public string PartitionKey { get; init; }

	/// <summary>Log position of this event or marker.</summary>
	public TFPos LogPosition { get; init; }

	/// <summary>If set, this is a checkpoint marker with this sequence number.</summary>
	public ulong? CheckpointMarkerSequence { get; init; }

	public bool IsCheckpointMarker => CheckpointMarkerSequence.HasValue;

	public static PartitionEvent ForEvent(ResolvedEvent @event, string partitionKey, TFPos logPosition)
		=> new() { Event = @event, PartitionKey = partitionKey, LogPosition = logPosition };

	public static PartitionEvent ForCheckpointMarker(ulong sequence, TFPos logPosition)
		=> new() { CheckpointMarkerSequence = sequence, LogPosition = logPosition };
}
```

### Step 2: Implement the dispatcher

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionDispatcher.cs
using System;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionDispatcher {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionDispatcher>();

	private readonly Channel<PartitionEvent>[] _partitionChannels;
	private readonly int _partitionCount;
	private readonly Func<ResolvedEvent, string> _getPartitionKey;
	private ulong _nextCheckpointSequence;

	public PartitionDispatcher(
		int partitionCount,
		Func<ResolvedEvent, string> getPartitionKey,
		int channelCapacity = 256) {
		_partitionCount = partitionCount;
		_getPartitionKey = getPartitionKey;

		_partitionChannels = new Channel<PartitionEvent>[partitionCount];
		for (int i = 0; i < partitionCount; i++) {
			_partitionChannels[i] = Channel.CreateBounded<PartitionEvent>(
				new BoundedChannelOptions(channelCapacity) {
					FullMode = BoundedChannelFullMode.Wait,
					SingleReader = true,
					SingleWriter = true
				});
		}
	}

	public ChannelReader<PartitionEvent> GetPartitionReader(int partitionIndex)
		=> _partitionChannels[partitionIndex].Reader;

	/// <summary>
	/// Dispatches a resolved event to the appropriate partition channel.
	/// </summary>
	public async ValueTask DispatchEvent(ResolvedEvent @event, TFPos logPosition, CancellationToken ct) {
		var partitionKey = _getPartitionKey(@event);
		var partitionIndex = GetPartitionIndex(partitionKey);
		var pe = PartitionEvent.ForEvent(@event, partitionKey, logPosition);
		await _partitionChannels[partitionIndex].Writer.WriteAsync(pe, ct);
	}

	/// <summary>
	/// Injects a checkpoint marker into all partition channels.
	/// Returns the marker sequence number.
	/// </summary>
	public async ValueTask<ulong> InjectCheckpointMarker(TFPos logPosition, CancellationToken ct) {
		var sequence = ++_nextCheckpointSequence;
		var marker = PartitionEvent.ForCheckpointMarker(sequence, logPosition);

		Log.Debug("Injecting checkpoint marker {Sequence} at {LogPosition}", sequence, logPosition);

		for (int i = 0; i < _partitionCount; i++) {
			await _partitionChannels[i].Writer.WriteAsync(marker, ct);
		}
		return sequence;
	}

	/// <summary>
	/// Completes all partition channels (no more events will be written).
	/// </summary>
	public void Complete(Exception ex = null) {
		for (int i = 0; i < _partitionCount; i++) {
			_partitionChannels[i].Writer.TryComplete(ex);
		}
	}

	private int GetPartitionIndex(string partitionKey) {
		if (_partitionCount == 1) return 0;
		var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(partitionKey));
		return (int)(hash % (uint)_partitionCount);
	}
}
```

### Step 3: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionEvent.cs
git add src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionDispatcher.cs
git commit -m "feat(projections-v2): add partition dispatcher with Chandy-Lamport markers"
```

---

## Task 5: Partition Processor

Each partition processes events sequentially, manages partition state, and collects output into buffers.

**Files:**
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/OutputBuffer.cs`
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionProcessor.cs`

### Step 1: Define the output buffer

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/OutputBuffer.cs
using System.Collections.Generic;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// Collects emitted events and state updates for one checkpoint interval.
/// </summary>
public class OutputBuffer {
	public List<EmittedEventEnvelope> EmittedEvents { get; } = new();

	/// <summary>
	/// Partition key → (stream name, state JSON, expected version).
	/// </summary>
	public Dictionary<string, (string StreamName, string StateJson, long ExpectedVersion)> DirtyStates { get; } = new();

	public TFPos LastLogPosition { get; set; }

	public void AddEmittedEvents(EmittedEventEnvelope[] events) {
		if (events is { Length: > 0 })
			EmittedEvents.AddRange(events);
	}

	public void SetPartitionState(string partitionKey, string streamName, string stateJson, long expectedVersion) {
		DirtyStates[partitionKey] = (streamName, stateJson, expectedVersion);
	}

	public void Clear() {
		EmittedEvents.Clear();
		DirtyStates.Clear();
		LastLogPosition = default;
	}
}
```

### Step 2: Implement the partition processor

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionProcessor.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionProcessor {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	private readonly int _partitionIndex;
	private readonly ChannelReader<PartitionEvent> _reader;
	private readonly IProjectionStateHandler _stateHandler;
	private readonly string _projectionName;
	private readonly Func<ulong, OutputBuffer, Task> _onCheckpointMarker;

	// Double-buffered output
	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();

	// In-memory state cache: partitionKey → stateJson
	private readonly Dictionary<string, string> _stateCache = new();

	public PartitionProcessor(
		int partitionIndex,
		ChannelReader<PartitionEvent> reader,
		IProjectionStateHandler stateHandler,
		string projectionName,
		Func<ulong, OutputBuffer, Task> onCheckpointMarker) {
		_partitionIndex = partitionIndex;
		_reader = reader;
		_stateHandler = stateHandler;
		_projectionName = projectionName;
		_onCheckpointMarker = onCheckpointMarker;
	}

	public async Task Run(CancellationToken ct) {
		Log.Debug("Partition {Index} starting for projection {Name}", _partitionIndex, _projectionName);

		await foreach (var pe in _reader.ReadAllAsync(ct)) {
			if (pe.IsCheckpointMarker) {
				await HandleCheckpointMarker(pe.CheckpointMarkerSequence!.Value, ct);
				continue;
			}

			ProcessEvent(pe);
		}
	}

	private void ProcessEvent(PartitionEvent pe) {
		var @event = pe.Event!.Value;
		var partitionKey = pe.PartitionKey;

		// Load state
		if (!_stateCache.TryGetValue(partitionKey, out var currentState))
			currentState = null; // TODO: load from stream on cache miss

		_stateHandler.Load(currentState ?? "{}");

		// Convert to projection ResolvedEvent
		var projEvent = ToProjectionResolvedEvent(@event);
		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		var processed = _stateHandler.ProcessEvent(
			partitionKey,
			checkpointTag,
			category: null,
			projEvent,
			out var newState,
			out var newSharedState,
			out var emittedEvents);

		if (processed && newState != null) {
			_stateCache[partitionKey] = newState;
			var stateStreamName = $"$projections-{_projectionName}-{partitionKey}-result";
			_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, -2); // ExpectedVersion.Any
		}

		_activeBuffer.AddEmittedEvents(emittedEvents);
		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private async Task HandleCheckpointMarker(ulong sequence, CancellationToken ct) {
		Log.Debug("Partition {Index} received checkpoint marker {Sequence}", _partitionIndex, sequence);

		// Swap buffers
		var bufferToFlush = _activeBuffer;
		_activeBuffer = _frozenBuffer;
		_activeBuffer.Clear();
		_frozenBuffer = bufferToFlush;

		// Report to coordinator
		await _onCheckpointMarker(sequence, bufferToFlush);
	}

	private static ResolvedEvent ToProjectionResolvedEvent(KurrentDB.Core.Data.ResolvedEvent @event) {
		var e = @event.Event;
		var link = @event.Link;
		return new ResolvedEvent(
			e.EventStreamId,
			e.EventNumber,
			link?.EventStreamId ?? e.EventStreamId,
			link?.EventNumber ?? e.EventNumber,
			@event.IsResolved,
			@event.OriginalPosition ?? new TFPos(e.LogPosition, e.TransactionPosition),
			e.EventId,
			e.EventType,
			e.IsJson,
			System.Text.Encoding.UTF8.GetString(e.Data.Span),
			System.Text.Encoding.UTF8.GetString(e.Metadata.Span));
	}
}
```

### Step 3: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/OutputBuffer.cs
git add src/KurrentDB.Projections.Core/Services/Processing/V2/PartitionProcessor.cs
git commit -m "feat(projections-v2): add partition processor with double-buffered output"
```

---

## Task 6: Checkpoint Coordinator

Collects frozen buffers from all partitions and writes atomically via multi-stream append.

**Files:**
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/CheckpointCoordinator.cs`

### Step 1: Implement the coordinator

The coordinator waits for all N partitions to report for the same marker sequence, then issues a single `ClientMessage.WriteEvents` with all emitted events, state updates, and the checkpoint event.

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/CheckpointCoordinator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class CheckpointCoordinator {
	private static readonly ILogger Log = Serilog.Log.ForContext<CheckpointCoordinator>();

	private readonly int _partitionCount;
	private readonly string _projectionName;
	private readonly string _checkpointStreamId;
	private readonly IPublisher _bus;
	private readonly ClaimsPrincipal _user;

	// Collected buffers for current checkpoint marker
	private readonly OutputBuffer[] _collectedBuffers;
	private ulong _currentMarkerSequence;
	private int _collectedCount;
	private TaskCompletionSource<TFPos> _checkpointTcs;

	// Outstanding checkpoint tracking (max 1 outstanding)
	private readonly SemaphoreSlim _checkpointSemaphore = new(1, 1);

	public CheckpointCoordinator(
		int partitionCount,
		string projectionName,
		IPublisher bus,
		ClaimsPrincipal user) {
		_partitionCount = partitionCount;
		_projectionName = projectionName;
		_checkpointStreamId = $"$projections-{projectionName}-checkpoint";
		_bus = bus;
		_user = user;
		_collectedBuffers = new OutputBuffer[partitionCount];
	}

	/// <summary>
	/// Called by each partition when it reaches a checkpoint marker.
	/// Thread-safe. Blocks until the checkpoint is written if this is the last partition to report.
	/// </summary>
	public async Task ReportPartitionCheckpoint(int partitionIndex, ulong markerSequence, OutputBuffer buffer) {
		bool allCollected;
		lock (_collectedBuffers) {
			if (_currentMarkerSequence != markerSequence) {
				_currentMarkerSequence = markerSequence;
				_collectedCount = 0;
				Array.Clear(_collectedBuffers);
			}

			_collectedBuffers[partitionIndex] = buffer;
			_collectedCount++;
			allCollected = _collectedCount == _partitionCount;
		}

		if (allCollected) {
			await WriteCheckpoint(markerSequence);
		}
	}

	private async Task WriteCheckpoint(ulong markerSequence) {
		await _checkpointSemaphore.WaitAsync();
		try {
			var lastPosition = _collectedBuffers.Max(b => b.LastLogPosition);

			Log.Information("Writing checkpoint {Sequence} for {Projection} at {Position}",
				markerSequence, _projectionName, lastPosition);

			// Build the multi-stream write
			var (streamIds, expectedVersions, events, streamIndexes) = BuildMultiStreamWrite(lastPosition);

			if (streamIds.Count == 0) {
				Log.Debug("Checkpoint {Sequence} has no events to write, skipping", markerSequence);
				return;
			}

			var tcs = new TaskCompletionSource<ClientMessage.WriteEventsCompleted>();
			var corrId = Guid.NewGuid();

			var writeMsg = new ClientMessage.WriteEvents(
				internalCorrId: corrId,
				correlationId: corrId,
				envelope: new CallbackEnvelope(msg => {
					if (msg is ClientMessage.WriteEventsCompleted completed)
						tcs.TrySetResult(completed);
					else
						tcs.TrySetException(new Exception($"Unexpected response: {msg.GetType().Name}"));
				}),
				requireLeader: true,
				eventStreamIds: streamIds.ToArray(),
				expectedVersions: expectedVersions.ToArray(),
				events: events.ToArray(),
				eventStreamIndexes: streamIndexes.ToArray(),
				user: _user);

			_bus.Publish(writeMsg);

			var result = await tcs.Task;
			if (result.Result != OperationResult.Success) {
				throw new Exception($"Checkpoint write failed: {result.Result} — {result.Message}");
			}

			Log.Debug("Checkpoint {Sequence} written successfully for {Projection}", markerSequence, _projectionName);
		} finally {
			lock (_collectedBuffers) {
				Array.Clear(_collectedBuffers);
				_collectedCount = 0;
			}
			_checkpointSemaphore.Release();
		}
	}

	private (List<string> streamIds, List<long> expectedVersions, List<Event> events, List<int> streamIndexes)
		BuildMultiStreamWrite(TFPos checkpointPosition) {

		var streamIds = new List<string>();
		var expectedVersions = new List<long>();
		var events = new List<Event>();
		var streamIndexes = new List<int>();

		// Stream index mapping: streamName → index in streamIds
		var streamIndexMap = new Dictionary<string, int>();

		int GetOrAddStreamIndex(string streamName, long expectedVersion) {
			if (!streamIndexMap.TryGetValue(streamName, out var idx)) {
				idx = streamIds.Count;
				streamIds.Add(streamName);
				expectedVersions.Add(expectedVersion);
				streamIndexMap[streamName] = idx;
			}
			return idx;
		}

		// 1. Checkpoint event
		var checkpointData = Encoding.UTF8.GetBytes(
			$"{{\"commitPosition\":{checkpointPosition.CommitPosition},\"preparePosition\":{checkpointPosition.PreparePosition}}}");
		var checkpointIdx = GetOrAddStreamIndex(_checkpointStreamId, ExpectedVersion.Any);
		events.Add(new Event(Guid.NewGuid(), "$ProjectionCheckpoint", true, checkpointData, null));
		streamIndexes.Add(checkpointIdx);

		// 2. Emitted events from all partitions
		foreach (var buffer in _collectedBuffers) {
			if (buffer == null) continue;
			foreach (var emitted in buffer.EmittedEvents) {
				var streamName = emitted.Event.StreamId;
				var idx = GetOrAddStreamIndex(streamName, ExpectedVersion.Any);
				var data = emitted.Event.Data != null ? Encoding.UTF8.GetBytes(emitted.Event.Data) : null;
				var metadata = emitted.Event.ExtraMetaData() != null
					? Encoding.UTF8.GetBytes(emitted.Event.ExtraMetaData())
					: null;
				events.Add(new Event(
					emitted.Event.EventId,
					emitted.Event.EventType,
					emitted.Event.IsJson,
					data,
					metadata));
				streamIndexes.Add(idx);
			}
		}

		// 3. State updates from all partitions
		foreach (var buffer in _collectedBuffers) {
			if (buffer == null) continue;
			foreach (var (partitionKey, (streamName, stateJson, expVer)) in buffer.DirtyStates) {
				var idx = GetOrAddStreamIndex(streamName, expVer);
				var stateData = Encoding.UTF8.GetBytes(stateJson);
				events.Add(new Event(Guid.NewGuid(), "Result", true, stateData, null));
				streamIndexes.Add(idx);
			}
		}

		return (streamIds, expectedVersions, events, streamIndexes);
	}
}
```

### Step 2: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/CheckpointCoordinator.cs
git commit -m "feat(projections-v2): add checkpoint coordinator with atomic multi-stream write"
```

---

## Task 7: Wire Up the Engine Pipeline

Connect all components in `ProjectionEngineV2`: reader → dispatcher → partitions → coordinator.

**Files:**
- Modify: `src/KurrentDB.Projections.Core/Services/Processing/V2/ProjectionEngineV2.cs`

### Step 1: Implement the full pipeline

Replace the skeleton `Run` method with the full pipeline:

```csharp
// Replace the entire ProjectionEngineV2.cs with:
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2 : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config;
	private readonly IReadStrategy _readStrategy;
	private readonly IPublisher _bus;
	private readonly ClaimsPrincipal _user;
	private CancellationTokenSource _cts;
	private Task _runTask;

	public ProjectionEngineV2(
		ProjectionEngineV2Config config,
		IReadStrategy readStrategy,
		IPublisher bus,
		ClaimsPrincipal user) {
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
		_bus = bus ?? throw new ArgumentNullException(nameof(bus));
		_user = user;
	}

	public Task Start(TFPos checkpoint, CancellationToken ct) {
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_runTask = Task.Run(() => Run(checkpoint, _cts.Token), _cts.Token);
		return Task.CompletedTask;
	}

	public bool IsFaulted => _runTask?.IsFaulted ?? false;
	public Exception FaultException => _runTask?.Exception?.InnerException;

	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint} with {Partitions} partitions",
			_config.ProjectionName, checkpoint, _config.PartitionCount);

		var dispatcher = new PartitionDispatcher(
			_config.PartitionCount,
			GetPartitionKeyFunc(),
			channelCapacity: 256);

		var coordinator = new CheckpointCoordinator(
			_config.PartitionCount,
			_config.ProjectionName,
			_bus,
			_user);

		// Start partition processors
		var partitionTasks = new Task[_config.PartitionCount];
		for (int i = 0; i < _config.PartitionCount; i++) {
			var processor = new PartitionProcessor(
				partitionIndex: i,
				reader: dispatcher.GetPartitionReader(i),
				stateHandler: _config.StateHandler,
				projectionName: _config.ProjectionName,
				onCheckpointMarker: (seq, buf) => coordinator.ReportPartitionCheckpoint(i, seq, buf));
			partitionTasks[i] = Task.Run(() => processor.Run(ct), ct);
		}

		// Main read loop
		try {
			long eventsProcessed = 0;
			long bytesProcessed = 0;
			var lastCheckpointTime = DateTime.UtcNow;
			TFPos lastPosition = checkpoint;

			await foreach (var response in _readStrategy.ReadFrom(checkpoint, ct)) {
				ct.ThrowIfCancellationRequested();

				switch (response) {
					case ReadResponse.EventReceived eventReceived:
						var @event = eventReceived.Event;
						var pos = @event.OriginalPosition!.Value;
						lastPosition = pos;

						await dispatcher.DispatchEvent(@event, pos, ct);
						eventsProcessed++;
						bytesProcessed += @event.Event.Data.Length + @event.Event.Metadata.Length;

						// Check if checkpoint needed
						if (ShouldCheckpoint(eventsProcessed, bytesProcessed, lastCheckpointTime)) {
							await dispatcher.InjectCheckpointMarker(lastPosition, ct);
							eventsProcessed = 0;
							bytesProcessed = 0;
							lastCheckpointTime = DateTime.UtcNow;
						}
						break;

					case ReadResponse.CheckpointReceived:
						// Checkpoint from the reader (filtered subscription)
						break;
				}
			}
		} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			Log.Information("ProjectionEngineV2 {Name} stopped", _config.ProjectionName);
		} catch (Exception ex) {
			Log.Error(ex, "ProjectionEngineV2 {Name} faulted", _config.ProjectionName);
			throw;
		} finally {
			dispatcher.Complete();
			await Task.WhenAll(partitionTasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	private bool ShouldCheckpoint(long eventsProcessed, long bytesProcessed, DateTime lastCheckpointTime) {
		var elapsed = DateTime.UtcNow - lastCheckpointTime;

		if (elapsed.TotalMilliseconds < _config.CheckpointAfterMs)
			return false;

		if (_config.CheckpointHandledThreshold > 0 && eventsProcessed >= _config.CheckpointHandledThreshold)
			return true;

		if (_config.CheckpointUnhandledBytesThreshold > 0 && bytesProcessed >= _config.CheckpointUnhandledBytesThreshold)
			return true;

		return false;
	}

	private Func<ResolvedEvent, string> GetPartitionKeyFunc() {
		if (_config.SourceDefinition.ByCustomPartitions) {
			// partitionBy is defined in JS — use state handler to determine partition
			return @event => {
				var e = @event.Event;
				var pos = @event.OriginalPosition ?? new TFPos(e.LogPosition, e.TransactionPosition);
				var tag = Checkpointing.CheckpointTag.FromPosition(0, pos.CommitPosition, pos.PreparePosition);
				return _config.StateHandler.GetStatePartition(
					tag, null,
					PartitionProcessor.ToProjectionResolvedEvent(@event));
			};
		}

		if (_config.SourceDefinition.ByStreams) {
			return @event => @event.Event.EventStreamId;
		}

		// No partitioning — single partition
		return _ => "";
	}

	public async ValueTask DisposeAsync() {
		if (_cts is not null) {
			await _cts.CancelAsync();
			if (_runTask is not null) {
				try { await _runTask; } catch (OperationCanceledException) { }
			}
			_cts.Dispose();
		}
		await _readStrategy.DisposeAsync();
	}
}
```

### Step 2: Make `ToProjectionResolvedEvent` internal static in PartitionProcessor

Update `PartitionProcessor.cs` — change `private static` to `internal static` for the `ToProjectionResolvedEvent` method.

### Step 3: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/
git commit -m "feat(projections-v2): wire up full engine pipeline (reader → dispatcher → partitions → coordinator)"
```

---

## Task 8: Unit Tests — Partition Dispatcher

**Files:**
- Create: `src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/PartitionDispatcherTests.cs`

### Step 1: Write tests

```csharp
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.V2;
using Xunit;

namespace KurrentDB.Projections.Core.Tests.Services.Processing.V2;

public class PartitionDispatcherTests {
	[Fact]
	public async Task events_with_same_key_go_to_same_partition() {
		var dispatcher = new PartitionDispatcher(4, _ => "same-key");
		var @event = CreateEvent("stream-1");
		var pos = new TFPos(100, 100);

		await dispatcher.DispatchEvent(@event, pos, CancellationToken.None);
		await dispatcher.DispatchEvent(@event, pos, CancellationToken.None);
		dispatcher.Complete();

		// Find which partition got events
		int partitionWithEvents = -1;
		for (int i = 0; i < 4; i++) {
			if (dispatcher.GetPartitionReader(i).TryRead(out _)) {
				Assert.Equal(-1, partitionWithEvents); // only one partition should have events
				partitionWithEvents = i;
				Assert.True(dispatcher.GetPartitionReader(i).TryRead(out _));
			}
		}
		Assert.NotEqual(-1, partitionWithEvents);
	}

	[Fact]
	public async Task checkpoint_marker_sent_to_all_partitions() {
		var dispatcher = new PartitionDispatcher(3, e => e.Event.EventStreamId);
		var pos = new TFPos(100, 100);

		var seq = await dispatcher.InjectCheckpointMarker(pos, CancellationToken.None);
		dispatcher.Complete();

		Assert.Equal(1UL, seq);
		for (int i = 0; i < 3; i++) {
			Assert.True(dispatcher.GetPartitionReader(i).TryRead(out var pe));
			Assert.True(pe.IsCheckpointMarker);
			Assert.Equal(1UL, pe.CheckpointMarkerSequence);
		}
	}

	private static ResolvedEvent CreateEvent(string streamId) {
		// Create a minimal resolved event for testing
		// This will need to use the appropriate test helpers
		throw new System.NotImplementedException("Use project-specific test helpers to create ResolvedEvent");
	}
}
```

### Step 2: Run tests

Run: `dotnet test src/KurrentDB.Projections.Core.Tests/ --filter "PartitionDispatcher" -v n`

### Step 3: Fix any issues and commit

```bash
git add src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/
git commit -m "test(projections-v2): add partition dispatcher unit tests"
```

---

## Task 9: Unit Tests — Output Buffer and Checkpoint Coordinator

**Files:**
- Create: `src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/OutputBufferTests.cs`
- Create: `src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/CheckpointCoordinatorTests.cs`

### Step 1: Write output buffer tests

```csharp
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.V2;
using Xunit;

namespace KurrentDB.Projections.Core.Tests.Services.Processing.V2;

public class OutputBufferTests {
	[Fact]
	public void clear_resets_all_collections() {
		var buffer = new OutputBuffer();
		buffer.SetPartitionState("key1", "stream1", "{}", -2);
		buffer.LastLogPosition = new TFPos(100, 100);

		buffer.Clear();

		Assert.Empty(buffer.DirtyStates);
		Assert.Empty(buffer.EmittedEvents);
		Assert.Equal(default, buffer.LastLogPosition);
	}

	[Fact]
	public void set_partition_state_overwrites_previous() {
		var buffer = new OutputBuffer();
		buffer.SetPartitionState("key1", "stream1", "{\"v\":1}", -2);
		buffer.SetPartitionState("key1", "stream1", "{\"v\":2}", -2);

		Assert.Single(buffer.DirtyStates);
		Assert.Equal("{\"v\":2}", buffer.DirtyStates["key1"].StateJson);
	}
}
```

### Step 2: Run tests

Run: `dotnet test src/KurrentDB.Projections.Core.Tests/ --filter "OutputBuffer" -v n`

### Step 3: Commit

```bash
git add src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/
git commit -m "test(projections-v2): add output buffer and checkpoint coordinator tests"
```

---

## Task 10: Management Layer Integration

Add engine version routing to `ManagedProjection` so projections can be configured to use v1 or v2.

**Files:**
- Modify: `src/KurrentDB.Projections.Core/Services/Management/ManagedProjection.cs` (PersistedState class, ~line 44)
- Create: `src/KurrentDB.Projections.Core/Services/Processing/V2/V2ProjectionProcessingStrategy.cs`

### Step 1: Add engine version to PersistedState

Read `ManagedProjection.cs` lines 44-82 to understand `PersistedState`, then add:

```csharp
// Add to PersistedState class (inside ManagedProjection.cs, ~line 80)
public int EngineVersion { get; set; } // 1 = v1 (default), 2 = v2
```

### Step 2: Create V2 processing strategy

This bridges the v2 engine into the existing `ProjectionProcessingStrategy` framework so `ManagedProjection` can route to it.

```csharp
// src/KurrentDB.Projections.Core/Services/Processing/V2/V2ProjectionProcessingStrategy.cs
using System;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// A processing strategy that creates a v2 engine instead of the v1 pipeline.
/// This is a bridge between the existing management layer and the new engine.
/// </summary>
public class V2ProjectionProcessingStrategy : ProjectionProcessingStrategy {
	private readonly IProjectionStateHandler _stateHandler;
	private readonly ProjectionConfig _projectionConfig;
	private readonly IQuerySources _sourceDefinition;

	public V2ProjectionProcessingStrategy(
		string name,
		ProjectionVersion projectionVersion,
		IProjectionStateHandler stateHandler,
		ProjectionConfig projectionConfig,
		IQuerySources sourceDefinition,
		ILogger logger)
		: base(name, projectionVersion, logger) {
		_stateHandler = stateHandler;
		_projectionConfig = projectionConfig;
		_sourceDefinition = sourceDefinition;
	}

	// NOTE: The actual integration with ManagedProjection's lifecycle
	// (Start, Stop, GetStatistics etc.) will be implemented in a follow-up task.
	// This strategy currently serves as a placeholder for the routing logic.

	public override bool GetStopOnEof() => false;
	public override bool GetUseCheckpoints() => _projectionConfig.CheckpointsEnabled;
	public override bool GetProducesRunningResults() => _sourceDefinition.ProducesResults;
	public override bool GetIsPartitioned() => _sourceDefinition.ByStreams || _sourceDefinition.ByCustomPartitions;

	protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
		IPublisher publisher, IPublisher inputQueue, Guid projectionCorrelationId,
		ProjectionNamesBuilder namingBuilder, PartitionStateCache partitionStateCache,
		CoreProjection coreProjection, IODispatcher ioDispatcher,
		IProjectionProcessingPhase firstPhase) {
		// V2 doesn't use the v1 processing phases
		throw new NotSupportedException("V2 engine does not use v1 processing phases");
	}
}
```

### Step 3: Update ProcessingStrategySelector

Modify `src/KurrentDB.Projections.Core/Services/Processing/Strategies/ProcessingStrategySelector.cs` to accept an engine version parameter. Read the file first, then add a new overload or modify the existing method.

Add after line 27:

```csharp
// Add engine version parameter
int engineVersion = 1,
```

And add inside the method body, before the existing return:

```csharp
if (engineVersion == 2) {
	return new V2.V2ProjectionProcessingStrategy(
		name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, _logger);
}
```

### Step 4: Commit

```bash
git add src/KurrentDB.Projections.Core/Services/Processing/V2/V2ProjectionProcessingStrategy.cs
git add src/KurrentDB.Projections.Core/Services/Management/ManagedProjection.cs
git add src/KurrentDB.Projections.Core/Services/Processing/Strategies/ProcessingStrategySelector.cs
git commit -m "feat(projections-v2): add engine version routing in management layer"
```

---

## Task 11: Build and Fix Compilation

**Files:**
- Various files in `src/KurrentDB.Projections.Core/Services/Processing/V2/`

### Step 1: Build the projections project

Run: `dotnet build src/KurrentDB.Projections.Core/ -c Release --framework=net10.0`

### Step 2: Fix any compilation errors

Common issues to expect:
- Missing `using` statements
- Type mismatches between `KurrentDB.Core.Data.ResolvedEvent` and `KurrentDB.Projections.Core.Services.Processing.ResolvedEvent`
- Missing methods or properties on referenced types
- Access modifiers needing adjustment

### Step 3: Build the test project

Run: `dotnet build src/KurrentDB.Projections.Core.Tests/ -c Release --framework=net10.0`

### Step 4: Run existing projection tests to check for regressions

Run: `dotnet test src/KurrentDB.Projections.Core.Tests/ -v n --no-build`

### Step 5: Commit fixes

```bash
git add -A
git commit -m "fix(projections-v2): fix compilation issues across v2 engine"
```

---

## Task 12: Integration Test — End-to-End Simple Projection

Create an integration test that runs a simple `fromAll` projection through the v2 engine.

**Files:**
- Create: `src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/ProjectionEngineV2IntegrationTests.cs`

### Step 1: Write integration test

This test will depend on the existing test infrastructure for setting up a mini-node. Study the patterns in:
- `src/KurrentDB.Projections.Core.Tests/Services/projections_manager/TestFixtureWithProjectionCoreAndManagementServices.cs`
- `src/KurrentDB.Core.Tests/Services/Transport/Enumerators/Enumerator.AllSubscriptionFiltered.Tests.cs`

The test should:
1. Write some events to streams
2. Create a v2 engine with a `fromAll` read strategy
3. Start the engine
4. Verify events are processed and state is written
5. Verify checkpoint is written atomically

### Step 2: Run the test

Run: `dotnet test src/KurrentDB.Projections.Core.Tests/ --filter "ProjectionEngineV2Integration" -v d`

### Step 3: Commit

```bash
git add src/KurrentDB.Projections.Core.Tests/Services/Processing/V2/
git commit -m "test(projections-v2): add end-to-end integration test for simple fromAll projection"
```

---

## Summary of Tasks

| Task | Component | Dependencies |
|------|-----------|-------------|
| 1 | Project skeleton (engine, config, IReadStrategy) | None |
| 2 | Stream-name-set EventFilter | None |
| 3 | Read strategies (FilteredAll, StreamSet, Factory) | Task 1, 2 |
| 4 | Partition dispatcher + channels | Task 1 |
| 5 | Partition processor + output buffer | Task 1, 4 |
| 6 | Checkpoint coordinator (multi-stream write) | Task 1, 5 |
| 7 | Wire up full pipeline | Task 3, 4, 5, 6 |
| 8 | Unit tests: dispatcher | Task 4 |
| 9 | Unit tests: output buffer + coordinator | Task 5, 6 |
| 10 | Management layer integration | Task 7 |
| 11 | Build + fix compilation | Task 10 |
| 12 | End-to-end integration test | Task 11 |

**Parallelizable:** Tasks 1-2, Tasks 4-5-6, Tasks 8-9

## Key Reference Files

| File | Purpose |
|------|---------|
| `src/KurrentDB.Core/Services/Transport/Enumerators/Enumerator.AllSubscriptionFiltered.cs` | Filtered subscription enumerator to wrap |
| `src/KurrentDB.Core/Services/Storage/ReaderIndex/EventFilter.cs` | Event filter implementations |
| `src/KurrentDB.Core/Messages/ClientMessage.cs:170-292` | Multi-stream WriteEvents message |
| `src/KurrentDB.Projections.Core/Services/IProjectionStateHandler.cs` | JS runtime interface (reused) |
| `src/KurrentDB.Projections.Core/Messages/IQuerySources.cs` | Source definition (fromAll/fromStreams/etc.) |
| `src/KurrentDB.Projections.Core/Services/Processing/Strategies/ProcessingStrategySelector.cs` | Strategy routing |
| `src/KurrentDB.Projections.Core/Services/Management/ManagedProjection.cs` | Management layer |
| `src/KurrentDB.Core/Messaging/CallbackEnvelope.cs` | Async callback pattern |
