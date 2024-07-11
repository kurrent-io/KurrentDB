// // ReSharper disable AccessToDisposedClosure
//
// using EventStore.Streaming.Connectors;
// using EventStore.Connectors.Control.Activation;
// using EventStore.Connectors.Control.Coordination;
// using EventStore.Toolkit.Testing.Fixtures;
// using Microsoft.Extensions.Configuration;
// using static EventStore.Connectors.Control.Activation.ConnectorsTaskManager;
//
// namespace EventStore.Extensions.Connectors.Tests.Control.Activation;
//
// class TestConnector : IConnector {
//     public ConnectorId ConnectorId => "test-connector-id";
//
//     public ConnectorState State { get; private set; } = ConnectorState.Unspecified;
//
//     public Task Connect(CancellationToken stoppingToken) {
//         State = ConnectorState.Running;
//         return Task.CompletedTask;
//     }
//
//     public Task Disconnect() {
//         State = ConnectorState.Stopped;
//         return Task.CompletedTask;
//     }
//
//     public async ValueTask DisposeAsync() => await Disconnect();
//
//
//     public void OverrideState(ConnectorState state) => State = state;
// }
//
// public class ConnectorsTaskManagerTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
//     static readonly CreateConnector CreateTestConnector = (_, _) => new TestConnector();
//
// 	[Fact]
// 	public async Task starts_process() {
//         // Arrange
//         var connector = new RegisteredConnector(
//             ConnectorId: ConnectorId.From(Guid.NewGuid()),
//             Revision: 1,
//             Settings: new(new Dictionary<string, string?>())
//         );
//
//         var expectedResult = new ConnectorProcessInfo(
//             connector.ConnectorId,
//             connector.Revision,
//             ConnectorState.Running
//         );
//
//         await using ConnectorsTaskManager sut = new(CreateTestConnector);
//
//         // Act
//         var result = await sut.StartProcess(connector.ConnectorId, connector.Revision, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//         // Assert
//         result.Should().BeEquivalentTo(expectedResult);
// 	}
//
//     [Fact]
//     public async Task stops_process() {
//         // Arrange
//         var connector = new RegisteredConnector(
//             ConnectorId: ConnectorId.From(Guid.NewGuid()),
//             Revision: 1,
//             Settings: new(new Dictionary<string, string?>())
//         );
//
//         var expectedResult = new ConnectorProcessInfo(
//             connector.ConnectorId,
//             connector.Revision,
//             ConnectorState.Stopped
//         );
//
//         await using ConnectorsTaskManager sut = new(CreateTestConnector);
//
//         await sut.StartProcess(connector.ConnectorId, connector.Revision, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//         // Act
//         var result = await sut.StopProcess(connector.ConnectorId);
//
//         // Assert
//         result.Should().BeEquivalentTo(expectedResult);
//     }
//
//     [Fact]
//     public async Task restarts_process_when_revision_is_different() {
//         // Arrange
//         var connector = new RegisteredConnector(
//             ConnectorId: ConnectorId.From(Guid.NewGuid()),
//             Revision: 1,
//             Settings: new(new Dictionary<string, string?>())
//         );
//
//         var expectedResult = new ConnectorProcessInfo(
//             connector.ConnectorId,
//             connector.Revision,
//             ConnectorState.Running
//         );
//
//         await using ConnectorsTaskManager sut = new(CreateTestConnector);
//
//         await sut.StartProcess(connector.ConnectorId, connector.Revision, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//         // Act
//         var result = await sut.StartProcess(connector.ConnectorId, connector.Revision, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//         // Assert
//         result.Should().BeEquivalentTo(expectedResult);
//     }
//
//     [Theory]
//     [InlineData(ConnectorState.Unspecified)]  // should not be possible
//     [InlineData(ConnectorState.Deactivating)] // must wait
//     [InlineData(ConnectorState.Stopped)]
//     public async Task restarts_process_if_not_running(ConnectorState stateOverride) {
//         // Arrange
//         var connector = new RegisteredConnector(
//             ConnectorId: ConnectorId.From(Guid.NewGuid()),
//             Revision: 1,
//             Settings: new(new Dictionary<string, string?>())
//         );
//
//         var expectedResult = new ConnectorProcessInfo(
//             connector.ConnectorId,
//             connector.Revision + 1,
//             ConnectorState.Running
//         );
//
//         await using var testProcessor = new TestConnector();
//
//         await using ConnectorsTaskManager sut = new((_, _) => testProcessor);
//
//         await sut.StartProcess(connector.ConnectorId, connector.Revision, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//
//         testProcessor.OverrideState(stateOverride);
//
//         // Act
//         var result = await sut.StartProcess(connector.ConnectorId, connector.Revision + 1, new ConfigurationManager().AddInMemoryCollection(connector.Settings).Build());
//
//         // Assert
//         result.Should().BeEquivalentTo(expectedResult);
//     }
//
//     [Fact]
//     public async Task stop_process_throws_when_connector_not_found() {
//         // Arrange
//         var connectorId = ConnectorId.From(Guid.NewGuid());
//
//         await using ConnectorsTaskManager sut = new(CreateTestConnector);
//
//         var action = async () => await sut.StopProcess(connectorId);
//
//         // Act & Assert
//         await action.Should().ThrowAsync<ConnectorProcessNotFoundException>()
//             .Where(e => e.ConnectorId == connectorId);
//     }
// }