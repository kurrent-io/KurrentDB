// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kurrent.Surge.Resilience;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Tests;
using KurrentDB.System.Testing;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[CollectionDefinition("SecondaryIndexingPluginDisabled")]
public sealed class SecondaryIndexingPluginDisabledDefinition : ICollectionFixture<SecondaryIndexingDisabledFixture>;

[CollectionDefinition("SecondaryIndexingPluginEnabled")]
public sealed class SecondaryIndexingPluginEnabledDefinition : ICollectionFixture<SecondaryIndexingEnabledFixture>;

[UsedImplicitly]
public class SecondaryIndexingEnabledFixture() : SecondaryIndexingFixture(true);

[UsedImplicitly]
public class SecondaryIndexingDisabledFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	private const string DatabasePathConfig = $"{KurrentConfigurationKeys.Prefix}:Database:Db";
	private const string PluginConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing";
	private const string OptionsConfigPrefix = $"{PluginConfigPrefix}:Options";
	protected string? PathName;
	private readonly TimeSpan _defaultTimeout = TimeSpan.FromMilliseconds(30000);

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		if (!isSecondaryIndexingPluginEnabled) return;

		SetUpDatabaseDirectory();

		Configuration = new() {
			{ $"{PluginConfigPrefix}:Enabled", "true" },
			{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CommitBatchSize)}", "2" },
			{ DatabasePathConfig, PathName }
		};

		OnTearDown = CleanUpDatabaseDirectory;
	}

	public async Task<List<ResolvedEvent>> ReadUntil(
		string streamName,
		int maxCount,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) {
		timeout ??= _defaultTimeout;
		var endTime = DateTime.UtcNow.Add(timeout.Value);

		var events = new List<ResolvedEvent>();
		ReadResponseException.StreamNotFound? streamNotFound = null;

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		do {
			try {
				events =
					await Publisher.ReadStream(
						streamName,
						StreamRevision.Start,
						maxCount,
						true,
						cancellationToken: cts.Token
					).ToListAsync(cts.Token);

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

	public async Task<List<ResolvedEvent>> SubscribeUntil(
		string streamName,
		int maxCount,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) {
		timeout ??= _defaultTimeout;

		var events = new List<ResolvedEvent>();

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		try {
			events = await SubscribeToStream(streamName, maxCount, cts.Token).Take(maxCount).ToListAsync(cts.Token);
		} catch (OperationCanceledException) {
			// can happen
		}

		return events;
	}

	private async IAsyncEnumerable<ResolvedEvent> SubscribeToStream(
		string streamName,
		int maxCount,
		[EnumeratorCancellation] CancellationToken ct = default
	) {
		var inboundChannel = Channel.CreateBounded<ReadResponse>(
			new BoundedChannelOptions(1000) {
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			}
		);
		await Publisher.SubscribeToStream(
			StreamRevision.Start,
			streamName,
			inboundChannel,
			DefaultRetryPolicies.ConstantBackoffPipelineBuilder().Build(),
			cancellationToken: ct
		);

		int count = 0;

		await foreach (var response in inboundChannel.Reader.ReadAllAsync(ct)) {
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

	public static Event ToEventData(string data) =>
		new(Guid.NewGuid(), "test", false, data, null, null);

	private void SetUpDatabaseDirectory() {
		var typeName = GetType().Name.Length > 30 ? GetType().Name[..30] : GetType().Name;
		PathName = Path.Combine(Path.GetTempPath(), $"ES-{Guid.NewGuid()}-{typeName}");

		Directory.CreateDirectory(PathName);
	}

	private Task CleanUpDatabaseDirectory() =>
		PathName != null ? DirectoryDeleter.TryForceDeleteDirectoryAsync(PathName, retries: 10) : Task.CompletedTask;
}
