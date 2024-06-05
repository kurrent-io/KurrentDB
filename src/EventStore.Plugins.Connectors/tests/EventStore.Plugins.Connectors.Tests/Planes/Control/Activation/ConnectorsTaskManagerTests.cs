// ReSharper disable AccessToDisposedClosure

using EventStore.Connectors.Control;
using EventStore.Connectors.Control.Activation;
using EventStore.Connectors.Control.Coordination;
using EventStore.Streaming;
using EventStore.Streaming.Connectors;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Routing;
using EventStore.Testing.Fixtures;
using static EventStore.Connectors.Control.Activation.ConnectorsTaskManager;

namespace EventStore.Plugins.Connectors.Tests.Control.Activation;

class TestProcessor : IProcessor {
    public string                            ProcessorId      { get; } = "test-processor-id";
    public string                            ProcessorName    { get; } = "test-processor-name";
    public string                            SubscriptionName { get; } = "test-processor-subscription-name";
    public string[]                          Streams          { get; } = [];
    public ConsumeFilter                     Filter           { get; } = ConsumeFilter.None;
    public IReadOnlyCollection<EndpointInfo> Endpoints        { get; } = [];
    public Type                              ProcessorType    { get; } = typeof(TestProcessor);

    public ProcessorState State   { get; private set; } = ProcessorState.Unspecified;
    public Task           Stopped { get; private set; } = Task.CompletedTask;

    public Task Activate(CancellationToken stoppingToken) {
        State = ProcessorState.Running;
        return Task.CompletedTask;
    }

    public Task Suspend() => throw new NotImplementedException();

    public Task Resume() => throw new NotImplementedException();

    public Task Deactivate() {
        State = ProcessorState.Stopped;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await Deactivate();


    public void OverrideState(ProcessorState state) => State = state;
}

public class ConnectorsTaskManagerTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
    static readonly CreateConnectorInstance CreateTestConnector = (_, _) => new TestProcessor();

	[Fact]
	public async Task starts_process() {
        // Arrange
        var connector = new RegisteredConnector(
            ConnectorId: ConnectorId.From(Guid.NewGuid()),
            Revision: 1,
            Settings: new(new Dictionary<string, string?>()),
            Position: LogPosition.Latest,
            Timestamp: DateTimeOffset.UtcNow
        );

        var expectedResult = new ConnectorProcessInfo(
            connector.ConnectorId,
            connector.Revision,
            ProcessorState.Running
        );

        await using ConnectorsTaskManager sut = new(CreateTestConnector);

        // Act
        var result = await sut.StartProcess(connector.ConnectorId, connector.Revision, connector.Settings);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
	}

    [Fact]
    public async Task stops_process() {
        // Arrange
        var connector = new RegisteredConnector(
            ConnectorId: ConnectorId.From(Guid.NewGuid()),
            Revision: 1,
            Settings: new(new Dictionary<string, string?>()),
            Position: LogPosition.Latest,
            Timestamp: DateTimeOffset.UtcNow
        );

        var expectedResult = new ConnectorProcessInfo(
            connector.ConnectorId,
            connector.Revision,
            ProcessorState.Stopped
        );

        await using ConnectorsTaskManager sut = new(CreateTestConnector);

        await sut.StartProcess(connector.ConnectorId, connector.Revision, connector.Settings);

        // Act
        var result = await sut.StopProcess(connector.ConnectorId);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task restarts_process_when_revision_is_different() {
        // Arrange
        var connector = new RegisteredConnector(
            ConnectorId: ConnectorId.From(Guid.NewGuid()),
            Revision: 1,
            Settings: new(new Dictionary<string, string?>()),
            Position: LogPosition.Latest,
            Timestamp: DateTimeOffset.UtcNow
        );

        var expectedResult = new ConnectorProcessInfo(
            connector.ConnectorId,
            connector.Revision,
            ProcessorState.Running
        );

        await using ConnectorsTaskManager sut = new(CreateTestConnector);

        await sut.StartProcess(connector.ConnectorId, connector.Revision, connector.Settings);

        // Act
        var result = await sut.StartProcess(connector.ConnectorId, connector.Revision, connector.Settings);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Theory]
    [InlineData(ProcessorState.Unspecified)]  // should not be possible
    [InlineData(ProcessorState.Suspended)]    // must resume
    [InlineData(ProcessorState.Deactivating)] // must wait
    [InlineData(ProcessorState.Stopped)]
    public async Task restarts_process_if_not_running(ProcessorState stateOverride) {
        // Arrange
        var connector = new RegisteredConnector(
            ConnectorId: ConnectorId.From(Guid.NewGuid()),
            Revision: 1,
            Settings: new(new Dictionary<string, string?>()),
            Position: LogPosition.Latest,
            Timestamp: DateTimeOffset.UtcNow
        );

        var expectedResult = new ConnectorProcessInfo(
            connector.ConnectorId,
            connector.Revision + 1,
            ProcessorState.Running
        );

        await using var                   testProcessor = new TestProcessor();
        await using ConnectorsTaskManager sut           = new((_, _) => testProcessor);

        await sut.StartProcess(connector.ConnectorId, connector.Revision, connector.Settings);


        testProcessor.OverrideState(stateOverride);

        // Act
        var result = await sut.StartProcess(connector.ConnectorId, connector.Revision + 1, connector.Settings);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task stop_process_throws_when_connector_not_found() {
        // Arrange
        var connectorId = ConnectorId.From(Guid.NewGuid());

        await using ConnectorsTaskManager sut = new(CreateTestConnector);

        var action = async () => await sut.StopProcess(connectorId);

        // Act & Assert
        await action.Should().ThrowAsync<ConnectorProcessNotFoundException>()
            .Where(e => e.ConnectorId == connectorId);
    }
}