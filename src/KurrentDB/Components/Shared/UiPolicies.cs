// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace KurrentDB.Components.Shared;

// Named authorization policies for UI pages, each mapped to a KurrentDB Operation via OperationRequirement.
// Pages reference these with [Authorize(Policy = UiPolicies.X)], so authorization is a single declaration at
// the top of the page, enforced by the framework before the page renders.
public static class UiPolicies {
	public const string ViewLogs = "ui:view-logs";
	public const string ViewConfiguration = "ui:view-configuration";
	public const string ViewSubsystems = "ui:view-subsystems";
	public const string ViewDatabaseStats = "ui:view-database-stats";
	public const string ViewUsers = "ui:view-users";
	public const string ViewScavenges = "ui:view-scavenges";
	public const string RunQueries = "ui:run-queries";
	public const string EditServerInfo = "ui:edit-server-info";

	const string ServerInfoStream = "$server-info";

	public static void Configure(AuthorizationOptions options) {
		var runQueries = UiOperations.ReadAll;

		Register(options, ViewLogs, new Operation(Operations.Node.Information.ReadLogs));
		Register(options, ViewConfiguration, new Operation(Operations.Node.Information.Options));
		Register(options, ViewSubsystems, new Operation(Operations.Node.Information.Subsystems));
		Register(options, ViewDatabaseStats, runQueries);
		Register(options, ViewUsers, new Operation(Operations.Users.List));
		Register(options, ViewScavenges, new Operation(Operations.Node.Scavenge.Read));
		Register(options, RunQueries, runQueries);
		Register(options, EditServerInfo, new Operation(Operations.Streams.Write)
			.WithParameter(Operations.Streams.Parameters.StreamId(ServerInfoStream)));
	}

	static void Register(AuthorizationOptions options, string name, Operation operation) =>
		options.AddPolicy(name, policy => policy.AddRequirements(new OperationRequirement(operation)));
}
