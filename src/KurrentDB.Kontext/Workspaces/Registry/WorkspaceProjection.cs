// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Threading.Channels;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Enumerators;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Runtime;

namespace KurrentDB.Kontext.Workspaces.Registry;

public sealed class WorkspaceProjection(
	ISystemClient client,
	WorkspaceRegistry workspaces,
	WorkspaceLifecycleManager lifecycle,
	ILogger<WorkspaceProjection> logger)
	: BackgroundService {

	static readonly string CreatedSchemaName = "$" + nameof(WorkspaceCreated);
	static readonly string StartedSchemaName = "$" + nameof(WorkspaceStarted);
	static readonly string StoppedSchemaName = "$" + nameof(WorkspaceStopped);
	static readonly string DeletedSchemaName = "$" + nameof(WorkspaceDeleted);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		const int retryDelaySec = 5;

		// give the server time to become ready before the first subscription attempt
		await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), stoppingToken);

		while (!stoppingToken.IsCancellationRequested) {
			try {
				await Tail(stoppingToken);
				return;
			} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
				return;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				logger.LogDebug(
					"Workspace projection waiting for the server to become ready, retrying in {Delay}s...", retryDelaySec);
				await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), stoppingToken);
			} catch (Exception ex) {
				logger.LogWarning(ex,
					"Workspace projection interrupted, retrying in {Delay}s...", retryDelaySec);
				await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), stoppingToken);
			}
		}
	}

	async Task Tail(CancellationToken ct) {
		var channel = Channel.CreateBounded<ReadResponse>(new BoundedChannelOptions(64) {
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait,
		});

		await client.Subscriptions.SubscribeToStream(
			revision: null,
			stream: WorkspaceNaming.ManagementAllStream,
			channel: channel,
			resiliencePipeline: ResiliencePipeline.Empty,
			cancellationToken: ct);

		var pending = new Dictionary<string, PendingState>(StringComparer.Ordinal);
		var caughtUp = false;

		await foreach (var response in channel.Reader.ReadAllAsync(ct)) {
			switch (response) {
				case ReadResponse.EventReceived eventReceived:
					var eventType = eventReceived.Event.OriginalEvent.EventType;
					var data = eventReceived.Event.OriginalEvent.Data;
					try {
						if (caughtUp)
							await Apply(eventType, data);
						else
							Accumulate(pending, eventType, data);
					} catch (Exception ex) {
						// A single malformed control-plane event must not stall the projection
						// forever (it would retry from the start and hit the same event). Skip it.
						logger.LogWarning(ex,
							"Skipping malformed workspace event '{EventType}' on stream '{Stream}' at {Position}",
							eventType, eventReceived.Event.OriginalEvent.EventStreamId,
							eventReceived.Event.OriginalEvent.EventNumber);
					}
					break;
				case ReadResponse.SubscriptionCaughtUp:
					await FlushPending(pending);
					logger.LogInformation(
						"Workspace registry caught up; {Count} workspace(s) tracked",
						workspaces.All.Count);
					caughtUp = true;
					break;
				case ReadResponse.StreamNotFound:
					return;
			}
		}
	}

	internal static void Accumulate(Dictionary<string, PendingState> pending, string eventType, ReadOnlyMemory<byte> data) {
		if (string.Equals(eventType, CreatedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceCreated>(data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			pending[evt.Name] = new PendingState {
				FilterRules = evt.FilterRules,
				FullTextIndexingEnabled = evt.FullTextIndexingEnabled,
				SemanticIndexingEnabled = evt.SemanticIndexingEnabled,
				DisableMemory = evt.DisableMemory,
				DisableImports = evt.DisableImports,
				DisableInquiries = evt.DisableInquiries,
				ReadOnly = evt.ReadOnly,
				Lifecycle = WorkspaceLifecycle.Created,
			};
		} else if (string.Equals(eventType, StartedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceStarted>(data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			if (pending.TryGetValue(evt.Name, out var state))
				state.Lifecycle = WorkspaceLifecycle.Started;
		} else if (string.Equals(eventType, StoppedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceStopped>(data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			if (pending.TryGetValue(evt.Name, out var state))
				state.Lifecycle = WorkspaceLifecycle.Stopped;
		} else if (string.Equals(eventType, DeletedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceDeleted>(data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			if (pending.TryGetValue(evt.Name, out var state))
				state.Lifecycle = WorkspaceLifecycle.Deleted;
		}
	}

	async Task FlushPending(Dictionary<string, PendingState> pending) {
		foreach (var (name, state) in pending) {
			if (state.Lifecycle == WorkspaceLifecycle.Deleted) {
				await lifecycle.Remove(name);
				continue;
			}

			workspaces.Upsert(WorkspaceEntry.Create(
				name, state.FilterRules,
				state.FullTextIndexingEnabled,
				state.SemanticIndexingEnabled,
				state.DisableMemory,
				state.DisableImports,
				state.DisableInquiries,
				state.ReadOnly));

			workspaces.SetStatus(name, state.Lifecycle);

			if (!workspaces.TryGet(name, out var entry))
				continue;

			if (state.Lifecycle == WorkspaceLifecycle.Started)
				lifecycle.Start(entry);
			else if (state.Lifecycle == WorkspaceLifecycle.Stopped)
				// Mount the stopped workspace's store so that it is readable
				// by the cross-workspace embedding cache
				lifecycle.Mount(entry);
		}

		pending.Clear();
	}

	async ValueTask Apply(string eventType, ReadOnlyMemory<byte> data) {
		if (string.Equals(eventType, CreatedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceCreated>(
				data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			workspaces.Upsert(WorkspaceEntry.Create(
				evt.Name, evt.FilterRules,
				evt.FullTextIndexingEnabled, evt.SemanticIndexingEnabled,
				evt.DisableMemory, evt.DisableImports, evt.DisableInquiries, evt.ReadOnly));
		} else if (string.Equals(eventType, StartedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceStarted>(
				data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			workspaces.SetStatus(evt.Name, WorkspaceLifecycle.Started);
			if (workspaces.TryGet(evt.Name, out var entry))
				lifecycle.Start(entry);
		} else if (string.Equals(eventType, StoppedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceStopped>(
				data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			workspaces.SetStatus(evt.Name, WorkspaceLifecycle.Stopped);
			await lifecycle.Stop(evt.Name);
		} else if (string.Equals(eventType, DeletedSchemaName, StringComparison.Ordinal)) {
			var evt = JsonSerializer.Deserialize<WorkspaceDeleted>(
				data.Span, WorkspaceJson.SerializerOptions);
			if (evt is null)
				return;
			await lifecycle.Remove(evt.Name);
			workspaces.Remove(evt.Name);
		}
	}

	internal sealed class PendingState {
		public required IReadOnlyList<FilterRule> FilterRules { get; init; }
		public required bool FullTextIndexingEnabled { get; init; }
		public required bool SemanticIndexingEnabled { get; init; }
		public required bool DisableMemory { get; init; }
		public required bool DisableImports { get; init; }
		public required bool DisableInquiries { get; init; }
		public required bool ReadOnly { get; init; }
		public WorkspaceLifecycle Lifecycle { get; set; }
	}
}

internal static class WorkspaceJson {
	public static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
	};
}
