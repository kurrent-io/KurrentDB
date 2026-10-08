// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Surge;
using KurrentDB.Surge.Eventuous;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB;

internal static class EmbeddedIndexRuntime {
	public static void ConfigureServices(IServiceCollection services) {
		// Index management needs the in-memory wire registry, not the Schema Registry service.
		services.AddSingleton(SchemaRegistry.Global)
			.AddSingleton<ISchemaRegistry>(sp => sp.GetRequiredService<SchemaRegistry>())
			.AddSingleton<ISchemaSerializer>(sp => sp.GetRequiredService<SchemaRegistry>());
		services.AddSurgeSystemComponents();
		services.AddEventStore<SystemEventStore>(sp => new SystemEventStore(
			sp.GetRequiredService<IReaderBuilder>().ReaderId("EmbeddedIndexReader").Create(),
			sp.GetRequiredService<IProducerBuilder>().ProducerId("EmbeddedIndexProducer").Create()));
	}
}
