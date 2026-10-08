// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#if KURRENT_EMBEDDED_MINIMAL
using Grpc.AspNetCore.Server;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.SecondaryIndexing.Indexes.User.Management;
using Microsoft.AspNetCore.Routing;

namespace KurrentDB.Embedded.Tests;

[Timeout(180_000)]
public class EmbeddedMinimalProfileTests {
	[ClassDataSource<EmbeddedDatabaseFixture>(Shared = SharedType.PerClass)]
	public required EmbeddedDatabaseFixture Fixture { get; init; }

	[Test]
	public void excluded_subsystems_are_not_loaded_or_mapped() {
		var names = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!).ToArray();
		names.ShouldNotContain(name => name.StartsWith("KurrentDB.Projections", StringComparison.Ordinal)
			|| name.Contains("Connectors", StringComparison.Ordinal)
			|| name.Contains("SchemaRegistry", StringComparison.Ordinal)
			|| name.StartsWith("KurrentDB.UI", StringComparison.Ordinal));
		var services = Fixture.Database.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.SelectMany(e => e.Metadata.OfType<GrpcMethodMetadata>()).Select(m => m.Method.ServiceName).ToArray();
		services.ShouldNotContain(name => name.Contains("persistent", StringComparison.OrdinalIgnoreCase)
			|| name.Contains("projection", StringComparison.OrdinalIgnoreCase)
			|| name.Contains("connector", StringComparison.OrdinalIgnoreCase)
			|| name.Contains("registry", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public void persistent_subscription_implementations_are_not_compiled() {
		var core = typeof(TFChunkDbConfig).Assembly;
		foreach (var name in new[] {
			"KurrentDB.Core.Services.PersistentSubscription.PersistentSubscriptionService`1",
			"KurrentDB.Core.Services.PersistentSubscription.PersistentSubscription",
			"KurrentDB.Core.Services.Transport.Grpc.PersistentSubscriptions",
			"KurrentDB.Core.Services.Transport.Http.Controllers.PersistentSubscriptionController"
		})
			core.GetType(name).ShouldBeNull();
	}

	[Test]
	public void index_management_resolves_without_schema_registry_service() =>
		Fixture.Database.Services.GetRequiredService<UserIndexCommandService>().ShouldNotBeNull();
}
#endif
