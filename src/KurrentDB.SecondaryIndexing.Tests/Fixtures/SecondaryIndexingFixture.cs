// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text;
using KurrentDB.Core;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Tests;
using KurrentDB.Surge.Testing;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[UsedImplicitly]
public class SecondaryIndexingEnabledFixture() : SecondaryIndexingFixture(true);

[UsedImplicitly]
public class SecondaryIndexingDisabledFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	private const string DatabasePathConfig = $"{KurrentConfigurationKeys.Prefix}:Database:Db";
	private const string PluginConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing";
	private const string OptionsConfigPrefix = $"{PluginConfigPrefix}:Options";
	private const string ProjectionsConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:Projections";

	private readonly TimeSpan _defaultTimeout = TimeSpan.FromMilliseconds(3000);
	private string? _path;

	public readonly int CommitSize = 500;

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		if (!isSecondaryIndexingPluginEnabled) return;

		SetUpDatabaseDirectory();

		Configuration = new() {
			{ $"{PluginConfigPrefix}:Enabled", "true" },
			{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CommitBatchSize)}", CommitSize.ToString() },
			{ DatabasePathConfig, _path },
			{ $"{ProjectionsConfigPrefix}:RunProjections", "None" }
		};

		OnTearDown = CleanUpDatabaseDirectory;
	}

	public async Task<List<ResolvedEvent>> ReadUntil(string indexName, int maxCount, bool forwards, Position? from = null, TimeSpan? timeout = null, CancellationToken ct = default) {
		timeout ??= _defaultTimeout;
		var endTime = DateTime.UtcNow.Add(timeout.Value);

		var events = new List<ResolvedEvent>();
		ReadResponseException.StreamNotFound? streamNotFound = null;

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		do {
			try {
				var start = from ?? (forwards ? Position.Start : Position.End);
				events = await Publisher.ReadIndex(indexName, start, maxCount, forwards: forwards, cancellationToken: cts.Token).ToListAsync(cts.Token);

				if (events.Count != maxCount) {
					await Task.Delay(25, cts.Token);
				}
			} catch (ReadResponseException.StreamNotFound ex) {
				streamNotFound = ex;
			} catch (OperationCanceledException) {
				// can happen
			}
		} while (events.Count != maxCount && DateTime.UtcNow < endTime);

		if (events.Count == 0 && streamNotFound != null)
			throw streamNotFound;

		return events;
	}

	public async Task<List<ResolvedEvent>> SubscribeUntil(string indexName, int maxCount, TimeSpan? timeout = null, CancellationToken ct = default) {
		timeout ??= _defaultTimeout;

		var events = new List<ResolvedEvent>();

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		try {
			await foreach (var evt in SubscribeToIndex(indexName, maxCount, cts.Token)) {
				events.Add(evt);
			}
			// events = await SubscribeToIndex(indexName, maxCount, cts.Token).Take(maxCount).ToListAsync(cts.Token);
		} catch (OperationCanceledException) {
			// can happen
		}

		return events;
	}

	private async IAsyncEnumerable<ResolvedEvent> SubscribeToIndex(string indexName, int maxCount, [EnumeratorCancellation] CancellationToken ct = default) {
		var enumerable = Publisher.SubscribeToIndex(indexName, Position.Start, cancellationToken: ct);

		int count = 0;

		await foreach (var response in enumerable) {
			if (count == maxCount)
				yield break;

			if (response is not ReadResponse.EventReceived eventReceived) continue;

			count++;
			yield return eventReceived.Event;
		}
	}

	public Task<WriteEventsResult> AppendToStream(string stream, params Event[] events) =>
		Publisher.WriteEvents(stream, events);


	public Task<WriteEventsResult> DeleteStream(string stream) =>
		Publisher.DeleteStream(stream);


	public Task<WriteEventsResult> HardDeleteStream(string stream) =>
		Publisher.HardDeleteStream(stream);


	public Task<WriteEventsResult> AppendToStream(string stream, params string[] eventData) =>
		AppendToStream(stream, eventData.Select(ToEventData).ToArray());

	public static Event ToEventData(string data) => new(Guid.NewGuid(), "test", false, Encoding.UTF8.GetBytes(data), false, []);

	private void SetUpDatabaseDirectory() {
		var typeName = GetType().Name.Length > 30 ? GetType().Name[..30] : GetType().Name;
		_path = Path.Combine(Path.GetTempPath(), $"ES-{Guid.NewGuid()}-{typeName}");

		Directory.CreateDirectory(_path);
	}

	private Task CleanUpDatabaseDirectory() =>
		_path != null ? DirectoryDeleter.TryForceDeleteDirectoryAsync(_path, retries: 10) : Task.CompletedTask;
}
