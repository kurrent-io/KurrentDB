using System.Net;
using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.KontrolPlane;

[Collection("RaftKontroller")]
public class RaftKontrollerTests : DirectoryFixture<RaftKontrollerTests> {
	private static readonly IPEndPoint Address = new(IPAddress.Loopback, 3269);
	private readonly RaftKontroller _kontroller;

	public RaftKontrollerTests() {
		_kontroller = new(new RaftKontroller.Options {
			ListenAddress = Address,
			AppointmentExpiration = TimeSpan.FromDays(1), // elect leader just once
			ConnectionPoolCapacity = 10,
			WalOptions = new() {
				Location = Directory,
			},
			SingleNodeDeployment = true,
		}) {
			ReplicaSet = new TestReplicaSet(),
		};
	}

	private IKontroller Kontroller => _kontroller;

	[Fact]
	public async Task MainDatabasePresence() {
		var database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Empty(database.Nodes);
		Assert.Equal(Database.MainDatabaseId, database.Id);
		Assert.Null(database.LeaderAddress);
	}

	[Fact]
	public async Task UpdateMainDatabase() {
		var database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		database = database with { Description = "Descr" };
		await Kontroller.AddOrUpdateDatabaseAsync(database, TestToken);

		database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Equal("Descr", database.Description);
	}

	[Fact]
	public async Task AddRemoveDatabase() {
		const string databaseId = "test";
		await Kontroller.AddOrUpdateDatabaseAsync(new Database { Id = databaseId, Description = "Descr" }, TestToken);

		var database = await Kontroller.GetDatabaseAsync(databaseId, TestToken);
		Assert.NotNull(database);
		Assert.Equal("Descr", database.Description);
		Assert.Equal(databaseId, database.Id);

		var databases = await Kontroller.GetDatabasesAsync(TestToken);
		Assert.Contains(Database.MainDatabaseId, databases);
		Assert.Contains(databaseId, databases);

		Assert.True(await Kontroller.RemoveDatabaseAsync(databaseId, TestToken));
		database = await Kontroller.GetDatabaseAsync(databaseId, TestToken);
		Assert.Null(database);
	}

	[Fact]
	public async Task AddRemoveNode() {
		var expectedNode = new DatabaseNode {
			Address = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 3262),
			DatabaseId = Database.MainDatabaseId,
			IsReadOnlyReplica = true,
		};

		await Kontroller.AddOrUpdateDatabaseNodeAsync(expectedNode, TestToken);

		var database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Null(database.LeaderAddress);

		var actualNode = Assert.Single(database.Nodes);
		Assert.Equal(expectedNode, actualNode);

		await Kontroller.RemoveDatabaseNodeAsync(Database.MainDatabaseId, expectedNode.Address, TestToken);

		database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Empty(database.Nodes);
	}

	[Fact]
	public async Task UpdateNode() {
		var expectedNode = new DatabaseNode {
			Address = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 3262),
			DatabaseId = Database.MainDatabaseId,
			IsReadOnlyReplica = true,
		};

		await Kontroller.AddOrUpdateDatabaseNodeAsync(expectedNode, TestToken);

		var database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Null(database.LeaderAddress);

		var actualNode = Assert.Single(database.Nodes);
		Assert.Equal(expectedNode.IsReadOnlyReplica, actualNode.IsReadOnlyReplica);

		expectedNode = expectedNode with { IsReadOnlyReplica = false };
		await Kontroller.AddOrUpdateDatabaseNodeAsync(expectedNode, TestToken);

		database = await Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
		Assert.NotNull(database);
		Assert.Null(database.LeaderAddress);

		actualNode = Assert.Single(database.Nodes);
		Assert.Equal(expectedNode.IsReadOnlyReplica, actualNode.IsReadOnlyReplica);
	}

	[Fact]
	public async Task TrackDatabaseDescription() {
		await using var enumerator = Kontroller
			.ListenDatabaseAsync(Database.MainDatabaseId, TestToken)
			.GetAsyncEnumerator();

		Assert.True(await enumerator.MoveNextAsync());
		var database = enumerator.Current;
		Assert.Empty(database.Description);

		database = database with { Description = "Descr" };
		await Kontroller.AddOrUpdateDatabaseAsync(database, TestToken);

		Assert.True(await enumerator.MoveNextAsync());
		database = enumerator.Current;
		Assert.Equal("Descr", database.Description);
	}

	[Fact]
	public async Task TrackDatabaseNode() {
		await using var enumerator = Kontroller
			.ListenDatabaseAsync(Database.MainDatabaseId, TestToken)
			.GetAsyncEnumerator();

		Assert.True(await enumerator.MoveNextAsync());
		var database = enumerator.Current;
		Assert.Empty(database.Nodes);

		var expectedNode = new DatabaseNode {
			Address = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 3262),
			DatabaseId = Database.MainDatabaseId,
			IsReadOnlyReplica = true,
		};

		await Kontroller.AddOrUpdateDatabaseNodeAsync(expectedNode, TestToken);
		Assert.True(await enumerator.MoveNextAsync());
		database = enumerator.Current;

		Assert.Equal(expectedNode, Assert.Single(database.Nodes));
	}

	[Fact]
	public async Task TrackDatabaseRemoval() {
		const string databaseId = "test";
		await Kontroller.AddOrUpdateDatabaseAsync(new Database { Id = databaseId, Description = "Descr" }, TestToken);

		await using var enumerator = Kontroller
			.ListenDatabaseAsync(databaseId, TestToken)
			.GetAsyncEnumerator();

		Assert.True(await enumerator.MoveNextAsync());
		var database = enumerator.Current;
		Assert.Equal("Descr", database.Description);
		Assert.Empty(database.Nodes);

		Assert.True(await Kontroller.RemoveDatabaseAsync(databaseId, TestToken));
		Assert.False(await enumerator.MoveNextAsync());
	}

	public override async ValueTask DisposeAsync() {
		await _kontroller.StopAsync(TestToken);
		await _kontroller.DisposeAsync();
		await base.DisposeAsync();
	}

	public override async ValueTask InitializeAsync() {
		await base.InitializeAsync();
		await _kontroller.StartAsync(TestToken);
		await _kontroller.WaitForLeaderAsync(TestToken);
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private sealed class TestReplicaSet : IDatabaseReplicaSet {
		ValueTask<ReplicaState> IDatabaseReplicaSet.GetReplicaStateAsync(EndPoint address, CancellationToken token)
			=> ValueTask.FromException<ReplicaState>(new NotSupportedException());
	}
}
