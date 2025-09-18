// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.DuckDB;

public static class DuckDBSetupDIExtensions {
	public static void AddDuckDBSetup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]T>(this IServiceCollection services)
		where T : class, IDuckDBSetup {
		services.AddSingleton<IDuckDBSetup, T>();
	}
}
