using Bogus;

using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Surge.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;
using static KurrentDB.Protocol.Registry.V2.SchemaRegistryService;

[assembly: Timeout(20_000)]
[assembly: NotInParallel]

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public abstract class SchemaRegistryServerTestFixture : ITestStartEventReceiver, ITestEndEventReceiver {
	protected Faker Faker => TestingToolkitAutoWireUp.Faker;

	protected string                      FixtureName              { get; private set; } = null!;
	protected ILoggerFactory              LoggerFactory            { get; private set; } = null!;
	protected FakeTimeProvider            TimeProvider             { get; private set; } = null!;
	protected IServiceProvider            NodeServices             { get; private set; } = null!;
	protected SchemaRegistryServiceClient Client                   { get; private set; } = null!;
	protected ISchemaRegistry             SchemaRegistry           { get; private set; } = null!;
	protected DuckDBConnectionProvider    DuckDbConnectionProvider { get; private set; } = null!;
	SequenceIdGenerator                   SequenceIdGenerator      { get; } = new();

	public async ValueTask OnTestStart(BeforeTestContext beforeTestContext) {
		await TestingToolkitAutoWireUp.TestSetUp(beforeTestContext.TestContext);

		FixtureName              = beforeTestContext.TestContext.TestDetails.TestClass.Name;
		NodeServices             = SchemaRegistryServerAutoWireUp.NodeServices;
		Client                   = SchemaRegistryServerAutoWireUp.Client;
		LoggerFactory            = NodeServices.GetRequiredService<ILoggerFactory>();
		TimeProvider             = NodeServices.GetRequiredService<FakeTimeProvider>();
		SchemaRegistry           = NodeServices.GetRequiredService<ISchemaRegistry>();
		DuckDbConnectionProvider = NodeServices.GetRequiredKeyedService<DuckDBConnectionProvider>("schema-registry");
	}

	public async ValueTask OnTestEnd(AfterTestContext testContext) =>
		await TestingToolkitAutoWireUp.TestCleanUp(testContext);

	protected async ValueTask<SurgeRecord> CreateRecord<T>(T message, SchemaDataFormat dataFormat = SchemaDataFormat.Json, string? streamId = null) {
		var schemaName = $"{SchemaRegistryConventions.Streams.RegistryStreamPrefix}-{typeof(T).Name.Kebaberize()}";
		var schemaInfo = new SchemaInfo(schemaName, dataFormat);

		var data = await ((ISchemaSerializer)SchemaRegistry).Serialize(message, schemaInfo);

		ulong sequenceId = SequenceIdGenerator.FetchNext();

		var headers = new Headers();

		schemaInfo.InjectIntoHeaders(headers);

		return new SurgeRecord {
			Id = Guid.NewGuid(),
			Position = streamId is null
				? RecordPosition.ForLog(sequenceId)
				: RecordPosition.ForStream(streamId, StreamRevision.From((long)sequenceId), sequenceId),
			Timestamp = TimeProvider.GetUtcNow().UtcDateTime,
			SchemaInfo = schemaInfo,
			Data = data,
			Value = message!,
			ValueType = typeof(T),
			SequenceId = sequenceId,
			Headers = headers,
		};
	}

	protected async IAsyncEnumerable<SurgeRecord> GenerateRecords<T>(
		int recordCount = 3, string? streamId = null,
		Func<int, T, T>? configureMessage = null,
		Func<int, SurgeRecord, SurgeRecord>? configureRecord = null
	) where T : new() {
		for (var i = 1; i <= recordCount; i++) {
			var message = configureMessage is null ? new T() : configureMessage.Invoke(i, new T());
			var record = await CreateRecord(message, streamId: streamId);
			yield return configureRecord?.Invoke(i, record) ?? record;
		}
	}
}
