// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

/// <summary>
/// Registers the workspace-management event schemas with the schema registry so the
/// Eventuous CommandService can serialise/deserialise them. Run once during Kontext init.
/// </summary>
public sealed class RegisterWorkspaceEvents(IServiceProvider services) {
	public async ValueTask Run(CancellationToken ct) {
		await RegisterType<WorkspaceCreated>(ct);
		await RegisterType<WorkspaceStarted>(ct);
		await RegisterType<WorkspaceStopped>(ct);
		await RegisterType<WorkspaceDeleted>(ct);

		ValueTask<RegisteredSchema> RegisterType<T>(CancellationToken token) => services
			.GetRequiredService<ISchemaRegistry>()
			.RegisterSchema<T>(
				new SchemaInfo(SchemaName: "$" + typeof(T).Name, SchemaDataFormat.Json),
				cancellationToken: token);
	}
}