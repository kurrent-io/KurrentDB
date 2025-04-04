// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Plugins.Diagnostics;
using Microsoft.Extensions.Configuration;
using Serilog;
using static EventStore.Plugins.Diagnostics.PluginDiagnosticsDataCollectionMode;

namespace EventStore.Core.Telemetry;

public sealed class TelemetryService :
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<ElectionMessage.ElectionsDone>,
	IHandle<SystemMessage.ReplicaStateMessage>,
	IHandle<LeaderDiscoveryMessage.LeaderFound>,
	IDisposable {
	private static readonly ILogger Logger = Log.ForContext<TelemetryService>();

	private static readonly TimeSpan InitialInterval = TimeSpan.FromHours(1);
	private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
	private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(10);

	private readonly ClusterVNodeOptions _nodeOptions;
	private readonly IConfiguration _configuration;
	private readonly CancellationTokenSource _cts = new();
	private readonly IPublisher _publisher;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly long _startTime = TimeProvider.System.GetTimestamp();
	private readonly Guid _nodeId;
	private readonly TFChunkManager _manager;
	private readonly PluginDiagnosticsDataCollector _pluginDiagnosticsDataCollector;

	private VNodeState _nodeState;
	private int _epochNumber;
	private Guid _leaderId = Guid.Empty;
	private Guid _firstEpochId = Guid.Empty;

	public TelemetryService(
		TFChunkManager manager,
		ClusterVNodeOptions nodeOptions,
		IConfiguration configuration,
		IPublisher publisher,
		ITelemetrySink sink,
		IReadOnlyCheckpoint writerCheckpoint,
		Guid nodeId
	) {
		_manager = manager;
		_nodeOptions = nodeOptions;
		_configuration = configuration;
		_publisher = publisher;
		_writerCheckpoint = writerCheckpoint;
		_nodeId = nodeId;

		_pluginDiagnosticsDataCollector = PluginDiagnosticsDataCollector.Start(
			_nodeOptions.PlugableComponents.Select(x => x.DiagnosticsName).ToArray()
		);

		_ = Task.Run(async () => {
			try {
				await ProcessAsync(publisher, sink);
			} catch (Exception ex) when (ex is not OperationCanceledException) {
				Logger.Error(ex, "Telemetry loop stopped");
			}
		});
	}

	public void Dispose() {
		_cts.Cancel();
		_cts.Dispose();
	}

	// we send messages on the publisher, and receive responses directly to the channel
	// using the channel reduces chatter on the main queue.
	private async Task ProcessAsync(IPublisher publisher, ITelemetrySink sink) {
		var channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(500) {
			SingleReader = true,
			FullMode = BoundedChannelFullMode.DropOldest,
		});

		var envelope = new ChannelEnvelope(channel);
		var scheduleInitialCollect = TimerMessage.Schedule.Create(InitialInterval, envelope, new TelemetryMessage.Collect());
		var scheduleCollect = TimerMessage.Schedule.Create(Interval - FlushDelay, envelope, new TelemetryMessage.Collect());
		var scheduleFlush = TimerMessage.Schedule.Create(FlushDelay, envelope, new TelemetryMessage.Flush());
		var usageRequest = new TelemetryMessage.Request(envelope);

		publisher.Publish(scheduleInitialCollect);

		var data = new JsonObject();
		await foreach (var message in channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (message) {
				case TelemetryMessage.Collect:
					Handle(usageRequest);
					publisher.Publish(usageRequest);
					publisher.Publish(scheduleFlush);
					break;

				case TelemetryMessage.Response response:
					if (string.IsNullOrWhiteSpace(response.Root)) {
						data[response.Key] = response.Value;
						break;
					}

					if (data.TryGetPropertyValue(response.Root, out var existing) &&
						existing is JsonObject existingObject) {

						existingObject[response.Key] = response.Value;
						break;
					}

					data[response.Root] = new JsonObject {
						[response.Key] = response.Value
					};
					break;

				case TelemetryMessage.Flush:
					await sink.Flush(data, _cts.Token);
					data.Clear();
					publisher.Publish(scheduleCollect);
					break;
			}
		}
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		_nodeState = message.State;
	}

	public void Handle(ElectionMessage.ElectionsDone message) {
		_epochNumber = message.ProposalNumber;
		_leaderId = message.Leader.InstanceId;
	}
	public void Handle(SystemMessage.ReplicaStateMessage message) {
		_epochNumber = message.Leader.EpochNumber;
		_leaderId = message.Leader.InstanceId;
	}
	public void Handle(LeaderDiscoveryMessage.LeaderFound message) {
		_epochNumber = message.Leader.EpochNumber;
		_leaderId = message.Leader.InstanceId;
	}

	private void Handle(TelemetryMessage.Request message) {
		if (_firstEpochId == Guid.Empty)
			ReadFirstEpoch();

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"version", JsonValue.Create(VersionInfo.Version)));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"edition", JsonValue.Create(VersionInfo.Edition)));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"uptime", JsonValue.Create(TimeProvider.System.GetElapsedTime(_startTime))));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"cluster", new JsonObject {
				["leaderId"] = JsonValue.Create(_leaderId),
				["nodeId"] = JsonValue.Create(_nodeId),
				["nodeState"] = _nodeState.ToString(),
			}));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"configuration", new JsonObject {
				["clusterSize"] = _nodeOptions.Cluster.ClusterSize,
				["enableAtomPubOverHttp"] = _nodeOptions.Interface.EnableAtomPubOverHttp,
				["insecure"] = _nodeOptions.Application.Insecure,
				["runProjections"] = _nodeOptions.Projection.RunProjections.ToString(),
				["authorizationType"] = _nodeOptions.Auth.AuthorizationType,
				["authenticationType"] = _nodeOptions.Auth.AuthenticationType
			}));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"database", new JsonObject {
				["epochNumber"] = _epochNumber,
				["firstEpochId"] = _firstEpochId,
				["activeChunkNumber"] = _writerCheckpoint.Read() / _nodeOptions.Database.ChunkSize,
			}));

		var env = EnvironmentTelemetry.Collect(_nodeOptions);
		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"environment", new JsonObject {
				["os"] = env.Machine.OS,
				["coreCount"] = env.Machine.ProcessorCount,
				["isContainer"] = env.Container.IsContainer,
				["isKubernetes"] = env.Container.IsKubernetes,
				["processorArchitecture"] = env.Arch,
				["totalDiskSpace"] = env.Machine.TotalDiskSpace,
				["totalMemory"] = env.Machine.TotalMemory,
			}));

		_nodeOptions.PlugableComponents
			.SelectMany(plugin => _pluginDiagnosticsDataCollector
				.CollectedEvents(plugin.DiagnosticsName)
				.Where(evt => evt.CollectionMode == Snapshot))
			.ForEach(evt => {
				try {
					var payload = JsonSerializer.SerializeToNode(
						evt.Data.ToDictionary(kvp => LowerFirstLetter(kvp.Key), kvp => kvp.Value));
					message.Envelope.ReplyWith(new TelemetryMessage.Response(LowerFirstLetter(evt.Source), payload));
				} catch (Exception ex) {
					Logger.Warning(ex, "Failed to collect telemetry from pluggable component {Source}", evt.Source);
				}
			});

		_publisher.Publish(new GossipMessage.ReadGossip(new CallbackEnvelope(resp => OnGossipReceived(message.Envelope, resp))));

		{
			var extraTelemetry = _configuration.GetSection("EventStore:Telemetry").Get<Dictionary<string, string>>() ?? [];
			var payload = JsonSerializer.SerializeToNode(extraTelemetry.ToDictionary(kvp => LowerFirstLetter(kvp.Key), kvp => kvp.Value));
			message.Envelope.ReplyWith(new TelemetryMessage.Response(
				"telemetry", payload));
		}
	}

	private static string LowerFirstLetter(string x) {
		if (string.IsNullOrEmpty(x) || char.IsLower(x[0]))
			return x;

		return $"{char.ToLower(x[0])}{x[1..]}";
	}

	private static void OnGossipReceived(IEnvelope<TelemetryMessage.Response> envelope, Message message) {
		if (message is not GossipMessage.SendGossip gossip)
			return;

		var seeds = new JsonObject();

		foreach (var member in gossip.ClusterInfo.Members) {
			seeds[member.InstanceId.ToString()] = member.State.ToString();
		}

		envelope.ReplyWith(new TelemetryMessage.Response("gossip", seeds));
	}

	private void ReadFirstEpoch() {
		try {
			var chunk = _manager.GetChunkFor(0);
			var result = chunk.TryReadAt(0, false);

			if (!result.Success)
				return;

			var epoch = ((SystemLogRecord)result.LogRecord).GetEpochRecord();
			_firstEpochId = epoch.EpochId;
		} catch {
			// noop
		}
	}
}
