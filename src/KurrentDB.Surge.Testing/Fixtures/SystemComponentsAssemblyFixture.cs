// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Kurrent.Surge;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Surge.Consumers;
using KurrentDB.Surge.Processors;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using KurrentDB.Surge.Testing.Fixtures;
using KurrentDB.Surge.Testing.Xunit.Extensions.AssemblyFixture;
using Microsoft.Extensions.DependencyInjection;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

[assembly: TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(SystemComponentsAssemblyFixture))]

namespace KurrentDB.Surge.Testing.Fixtures;

[PublicAPI]
public partial class SystemComponentsAssemblyFixture : ClusterVNodeFixture {
	public SystemComponentsAssemblyFixture() {
		TimeProvider = new FakeTimeProvider();

		ConfigureServices = services => {
			services
				.AddSingleton<TimeProvider>(TimeProvider)
				.AddSingleton(LoggerFactory);
		};

		OnSetup = () => {
			Producer = NewProducer()
				.ProducerId("test-pdx")
				.Create();

			Reader = NewReader()
				.ReaderId("test-rdx")
				.Create();

			return Task.CompletedTask;
		};

		OnTearDown = async () => {
			await Producer.DisposeAsync();
			await Reader.DisposeAsync();
		};
	}

	public Kurrent.Surge.Schema.SchemaRegistry SchemaRegistry => NodeServices.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
	public ISchemaSerializer SchemaSerializer => SchemaRegistry;
	public IStateStore StateStore { get; private set; } = null!;
	public FakeTimeProvider TimeProvider { get; private set; } = null!;

	public IManager Manager => NodeServices.GetRequiredService<IManager>();

	public IProducer Producer { get; private set; } = null!;
	public IReader Reader { get; private set; } = null!;

	SequenceIdGenerator SequenceIdGenerator { get; } = new();

	public SystemProducerBuilder NewProducer() => SystemProducer.Builder
		.Publisher(Publisher)
		.LoggerFactory(LoggerFactory)
		.SchemaRegistry(SchemaRegistry);

	public SystemReaderBuilder NewReader() => SystemReader.Builder
		.Publisher(Publisher)
		.LoggerFactory(LoggerFactory)
		.SchemaRegistry(SchemaRegistry);

	public SystemConsumerBuilder NewConsumer() => SystemConsumer.Builder
		.Publisher(Publisher)
		.LoggerFactory(LoggerFactory)
		.SchemaRegistry(SchemaRegistry);

	public SystemProcessorBuilder NewProcessor() => SystemProcessor.Builder
		.Publisher(Publisher)
		.LoggerFactory(LoggerFactory)
		.SchemaRegistry(SchemaRegistry);

	public string NewIdentifier([CallerMemberName] string? name = null) =>
		$"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
}

public abstract class SystemComponentsIntegrationTests<TFixture> where TFixture : SystemComponentsAssemblyFixture {
    protected SystemComponentsIntegrationTests(ITestOutputHelper output, TFixture fixture) => Fixture = WithExtension.With(fixture, x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}

public abstract class SystemComponentsIntegrationTests(ITestOutputHelper output, SystemComponentsAssemblyFixture fixture)
    : SystemComponentsIntegrationTests<SystemComponentsAssemblyFixture>(output, fixture);
