// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.SchemaRegistry.Planes.Projection;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.Surge;
using KurrentDB.Surge.Eventuous;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static KurrentDB.SchemaRegistry.SchemaRegistryConventions;

namespace KurrentDB.SchemaRegistry;

public static class SchemaRegistryWireUp {
	public static IServiceCollection AddSchemaRegistryService(this IServiceCollection services) {
		services.AddNodeSystemInfoProvider();

		services.TryAddSingleton(TimeProvider.System);

		services.AddSingleton<GetUtcNow>(ctx => ctx.GetRequiredService<TimeProvider>().GetUtcNow);

		services.AddGrpc(x => x.EnableDetailedErrors = true);
		services.AddGrpcRequestValidation();

		services.AddSingleton<ISchemaCompatibilityManager>(new NJsonSchemaCompatibilityManager());

		services.AddDuckDBConnectionProvider();

		services.AddMessageRegistration();
		services.AddCommandPlane();
		services.AddQueryPlane();

		return services
			.AddSingleton(Kurrent.Surge.Schema.SchemaRegistry.Global)
			.AddSingleton<ISchemaRegistry>(ctx => ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>())
			.AddSingleton<ISchemaSerializer>(ctx => ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>());
	}

	static IServiceCollection AddCommandPlane(this IServiceCollection services) {
		services.AddEventStore<SystemEventStore>(ctx => {
				var reader = ctx.GetRequiredService<IReaderBuilder>()
					.ReaderId("EventuousReader")
					.Create();

				var producer = ctx.GetRequiredService<IProducerBuilder>()
					.ProducerId("EventuousProducer")
					.Create();

				var manager = ctx.GetRequiredService<SystemManager>();

				return new SystemEventStore(reader, producer, manager);
			}
		);

		// Domain services

		services.AddSingleton<CheckAccess>(_ =>
			context => {
				var http = context.GetHttpContext();
				var authenticated = http.User.Identity?.IsAuthenticated ?? false;
				return ValueTask.FromResult(authenticated);
			}
		);

		services.AddSingleton<LookupSchemaNameByVersionId>(ctx => {
			var queries = ctx.GetRequiredService<SchemaQueries>();
			return async (schemaVersionId, ct) => {
				var response = await queries.LookupSchemaName(new() { SchemaVersionId = schemaVersionId }, ct);
				return response.SchemaName;
			};
		});

		services.AddCommandService<SchemaApplication, SchemaEntity>();

		return services;
	}

	static IServiceCollection AddQueryPlane(this IServiceCollection services) {
		return services
			.AddSingleton<IHostedService, DuckDBProjectorService>()
			.AddSingleton<SchemaQueries>();
	}

	static IServiceCollection AddMessageRegistration(this IServiceCollection services) {
		return services.AddSchemaMessageRegistrationStartupTask(
			"Schema Registry Message Registration",
			RegisterManagementMessages
		);

		static async Task RegisterManagementMessages(ISchemaRegistry registry, CancellationToken ct) {
			Task[] tasks = [
				RegisterMessages<SchemaCreated>(registry, ct),
				RegisterMessages<SchemaTagsUpdated>(registry, ct),
				RegisterMessages<SchemaDescriptionUpdated>(registry, ct),
				RegisterMessages<SchemaCompatibilityModeChanged>(registry, ct),
				RegisterMessages<SchemaVersionRegistered>(registry, ct),
				RegisterMessages<SchemaVersionsDeleted>(registry, ct),
				RegisterMessages<SchemaDeleted>(registry, ct),
			];

			await tasks.WhenAll();
		}
	}

	public static IApplicationBuilder UseSchemaRegistryService(this IApplicationBuilder app) {
		app.UseRouting();

		app.UseEndpoints(endpoints => {
			endpoints.MapGrpcService<SchemaRegistryService>();
		});

		return app;
	}
}
