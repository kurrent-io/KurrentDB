using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using Serilog;

namespace EventStore.Core.Telemetry;

public sealed class TelemetryService : IDisposable,
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<ElectionMessage.ElectionsDone> {

	private static readonly ILogger _log = Log.ForContext<TelemetryService>();
	private static readonly TimeSpan _initialInterval = TimeSpan.FromHours(1);
	private static readonly TimeSpan _interval = TimeSpan.FromHours(24);
	private static readonly TimeSpan _flushDelay = TimeSpan.FromSeconds(10);

	private readonly ClusterVNodeOptions _nodeOptions;
	private readonly CancellationTokenSource _cts = new();
	private readonly IPublisher _publisher;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly DateTime _startTime = DateTime.UtcNow;
	private readonly Guid _nodeId;
	private readonly TFChunkManager _manager;

	private VNodeState _nodeState;
	private int _epochNumber;
	private Guid _leaderId = Guid.Empty;
	private Guid _firstEpochId = Guid.Empty;

	public TelemetryService(
		TFChunkManager manager,
		ClusterVNodeOptions nodeOptions,
		IPublisher publisher,
		ITelemetrySink sink,
		IReadOnlyCheckpoint writerCheckpoint,
		Guid nodeId) {

		_manager = manager;
		_nodeOptions = nodeOptions;
		_publisher = publisher;
		_writerCheckpoint = writerCheckpoint;
		_nodeId = nodeId;
		Task.Run(async () => {
			try {
				await ProcessAsync(publisher, sink).ConfigureAwait(false);
			} catch (Exception ex) when (ex is not OperationCanceledException) {
				_log.Error(ex, "Telemetry loop stopped");
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
		var scheduleInitialCollect = TimerMessage.Schedule.Create(_initialInterval, envelope, new TelemetryMessage.Collect());
		var scheduleCollect = TimerMessage.Schedule.Create(_interval - _flushDelay, envelope, new TelemetryMessage.Collect());
		var scheduleFlush = TimerMessage.Schedule.Create(_flushDelay, envelope, new TelemetryMessage.Flush());
		var usageRequest = new TelemetryMessage.Request(envelope);

		publisher.Publish(scheduleInitialCollect);

		var data = new JsonObject();
		await foreach (var message in channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
			switch (message) {
				case TelemetryMessage.Collect:
					Handle(usageRequest);
					publisher.Publish(usageRequest);
					publisher.Publish(scheduleFlush);
					break;

				case TelemetryMessage.Response response:
					data[response.Key] = response.Value;
					break;

				case TelemetryMessage.Flush:
					await sink.Flush(data, _cts.Token).ConfigureAwait(false);
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

	private void Handle(TelemetryMessage.Request message) {
		if (_firstEpochId == Guid.Empty)
			ReadFirstEpoch();

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"version", JsonValue.Create(VersionInfo.Version)));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"tag", JsonValue.Create(VersionInfo.Tag)));

		message.Envelope.ReplyWith(new TelemetryMessage.Response(
			"uptime", JsonValue.Create(DateTime.UtcNow - _startTime)));

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
				["enableExternalTcp"] = _nodeOptions.Interface.EnableExternalTcp,
				["insecure"] = _nodeOptions.Application.Insecure,
				["runProjections"] = _nodeOptions.Projections.RunProjections.ToString(),
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
				["coreCount"] = env.Machine.ProcessorCount,
				["isContainer"] = env.Container.IsContainer,
				["isKubernetes"] = env.Container.IsKubernetes,
				["processorArchitecture"] = env.Arch,
				["totalDiskSpace"] = env.Machine.TotalDiskSpace,
				["totalMemory"] = env.Machine.TotalMemory,
			}));

		_publisher.Publish(new GossipMessage.ReadGossip(new CallbackEnvelope(resp => OnGossipReceived(message.Envelope, resp))));
	}

	private static void OnGossipReceived(IEnvelope<TelemetryMessage.Response> envelope, Message message) {
		if (message is not GossipMessage.SendGossip gossip)
			return;

		var seeds = new JsonObject();

		foreach (var member in gossip.ClusterInfo.Members) {
			seeds.Add(new(member.InstanceId.ToString(), member.State.ToString()));
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
