using System;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Telemetry;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Telemetry;

public sealed class TelemetryServiceTests : IAsyncLifetime {
	private readonly TelemetryService _sut;
	private readonly InMemoryTelemetrySink _sink;
	private readonly ChannelReader<Message> _channelReader;
	private readonly TFChunkDb _db;
	private readonly DirectoryFixture<TelemetryServiceTests> _fixture = new();

	public TelemetryServiceTests() {
		var config = TFChunkHelper.CreateSizedDbConfig(_fixture.Directory, 0, chunkSize: 4096);
		_db = new TFChunkDb(config);
		_db.Open(false);
		var channel = Channel.CreateUnbounded<Message>();
		_channelReader = channel.Reader;
		_sink = new InMemoryTelemetrySink();
		_sut = new TelemetryService(
			_db.Manager,
			new ClusterVNodeOptions(),
			new EnvelopePublisher(new ChannelEnvelope(channel)),
			_sink,
			new InMemoryCheckpoint(0),
			Guid.NewGuid());
	}

	public Task InitializeAsync() => _fixture.InitializeAsync();

	public async Task DisposeAsync() {
		_sut.Dispose();
		_db.Close();
		await _fixture.DisposeAsync();
	}

	[Fact]
	public async Task can_collect_and_flush_telemetry() {
		// receive schedule of collect trigger it
		var schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);
		schedule.Reply();

		// receive the gossip request the telemetry service sends.
		Assert.IsType<GossipMessage.ReadGossip>(await _channelReader.ReadAsync());

		// receive usage request and send response
		var request = Assert.IsType<TelemetryMessage.Request>(await _channelReader.ReadAsync());
		request.Envelope.ReplyWith(new TelemetryMessage.Response(
			"foo",
			new JsonObject {
				["bar"] = 42,
			}));

		// receive schedule of flush and trigger it
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Flush>(schedule.ReplyMessage);
		schedule.Reply();

		// receive schedule of collect indicating flush is complete
		schedule = Assert.IsType<TimerMessage.Schedule>(await _channelReader.ReadAsync());
		Assert.IsType<TelemetryMessage.Collect>(schedule.ReplyMessage);

		// check sink has received the data
		Assert.NotNull(_sink.Data);
		Assert.NotNull(_sink.Data["foo"]);
		Assert.Equal(new JsonObject { ["bar"] = 42 }.ToString(), _sink.Data["foo"].ToString());
	}
}
