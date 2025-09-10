// 	// protected async ValueTask<SurgeRecord> CreateRecord<T>(T message, SchemaDataFormat dataFormat = SchemaDataFormat.Json, string? streamId = null) {
// 	// 	var schemaName = $"{SchemaRegistryConventions.Streams.RegistryStreamPrefix}-{typeof(T).Name.Kebaberize()}";
// 	// 	var schemaInfo = new SchemaInfo(schemaName, dataFormat);
// 	//
// 	// 	var data = await ((ISchemaSerializer)SchemaRegistry).Serialize(message, schemaInfo);
// 	//
// 	// 	ulong sequenceId = SequenceIdGenerator.FetchNext();
// 	//
// 	// 	var headers = new Headers();
// 	//
// 	// 	schemaInfo.InjectIntoHeaders(headers);
// 	//
// 	// 	return new() {
// 	// 		Id = Guid.NewGuid(),
// 	// 		Position = streamId is null
// 	// 			? RecordPosition.ForLog(sequenceId)
// 	// 			: RecordPosition.ForStream(streamId, StreamRevision.From((long)sequenceId), sequenceId),
// 	// 		Timestamp  = TimeProvider.GetUtcNow().UtcDateTime,
// 	// 		SchemaInfo = schemaInfo,
// 	// 		Data       = data,
// 	// 		Value      = message!,
// 	// 		ValueType  = typeof(T),
// 	// 		SequenceId = sequenceId,
// 	// 		Headers    = headers
// 	// 	};
// 	// }
// 	//
// 	// protected async IAsyncEnumerable<SurgeRecord> GenerateRecords<T>(
// 	// 	int recordCount = 3, string? streamId = null,
// 	// 	Func<int, T, T>? configureMessage = null,
// 	// 	Func<int, SurgeRecord, SurgeRecord>? configureRecord = null
// 	// ) where T : new() {
// 	// 	for (var i = 1; i <= recordCount; i++) {
// 	// 		var message = configureMessage is null ? new() : configureMessage.Invoke(i, new());
// 	// 		var record = await CreateRecord(message, streamId: streamId);
// 	// 		yield return configureRecord?.Invoke(i, record) ?? record;
// 	// 	}
// 	// }

// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// // ReSharper disable ArrangeTypeMemberModifiers
//
// using System.Net;
// using Grpc.Net.Client;
// using Kurrent.Toolkit.Testing.Logging;
// using KurrentDB.Core;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Time.Testing;
// using Serilog;
// using Serilog.Context;
// using Serilog.Core;
// using Serilog.Events;
// using Serilog.Extensions.Logging;
// using static KurrentDB.Protocol.V2.Streams.StreamsService;
// using ILogger = Microsoft.Extensions.Logging.ILogger;
//
// using FrameworkLogger = Microsoft.Extensions.Logging.ILogger;
//
// namespace KurrentDB.Api.Tests.Fixtures;
//
// public class ApiAutoWireUp {
// 	public static ClusterVNodeOptions NodeOptions  { get; private set; } = null!;
// 	public static IServiceProvider    NodeServices { get; private set; } = null!;
//
// 	public static StreamsServiceClient StreamsClient { get; private set; } = null!;
//
// 	static ClusterVNodeApp ClusterVNodeApp { get; set; } = null!;
// 	static GrpcChannel     GrpcChannel     { get; set; } = null!;
// 	static HttpClient      HttpClient      { get; set; } = null!;
//
// 	[Before(Assembly)]
// 	public static async Task AssemblySetUp(AssemblyHookContext context, CancellationToken cancellationToken) {
// 		ClusterVNodeApp = new();
//
// 		var (options, services) = await ClusterVNodeApp.Start(configureServices: ConfigureServices);
//
// 		var serverUrl = new Uri(ClusterVNodeApp.ServerUrls.First());
//
// 		NodeServices = services;
// 		NodeOptions  = options;
//
// 		Serilog.Log.Information("Test server started at {Url}", serverUrl);
//
// 		HttpClient = new HttpClient {
// 			BaseAddress = serverUrl,
// 			DefaultRequestVersion = HttpVersion.Version20,
// 			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
// 		};
//
// 		GrpcChannel = GrpcChannel.ForAddress(
// 			$"http://localhost:{serverUrl.Port}",
// 			new GrpcChannelOptions {
// 				HttpClient = HttpClient,
// 				LoggerFactory = services.GetRequiredService<ILoggerFactory>()
// 			}
// 		);
//
// 		StreamsClient = new KurrentDB.Protocol.Registry.V2.SchemaRegistryService.SchemaRegistryServiceClient(GrpcChannel);
// 	}
//
// 	static void ConfigureServices(IServiceCollection services) {
// 		var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
//
//         services.AddSingleton<ILoggerFactory>(new TestContextAwareLoggerFactory());
// 		services.AddSingleton<TimeProvider>(timeProvider);
// 		services.AddSingleton(timeProvider);
// 	}
//
// 	[After(Assembly)]
// 	public static async Task AssemblyCleanUp() {
// 		await ClusterVNodeApp.DisposeAsync();
// 		HttpClient.Dispose();
// 		GrpcChannel.Dispose();
// 	}
//
//
// }
//
//
// //
// //
// // public sealed class TestContextAwareLoggerProvider : ILoggerProvider, ILogEventEnricher, ISupportExternalScope {
// //
// // 	readonly ILoggerFactory _defaultLoggerFactory = new LoggerFactory();
// //
// // 	static readonly SerilogLoggerProvider DefaultProvider = new();
// //
// // 	public ILogger CreateLogger(string categoryName) {
// //
// // 		if (TestContext.Current is not null)
// // 			return TestContext.Current.Logger().ForContext().CreateLogger(categoryName);
// //
// // 		return DefaultProvider.CreateLogger(categoryName);
// // 	}
// //
// // 	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
// // 		DefaultProvider.Enrich(logEvent, propertyFactory);
// // 	}
// //
// // 	public void SetScopeProvider(IExternalScopeProvider scopeProvider) {
// // 		DefaultProvider.SetScopeProvider(scopeProvider);
// // 	}
// //
// // 	public void Dispose() { }
// // }
