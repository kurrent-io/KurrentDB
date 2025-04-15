// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core;

public interface IInternalStartup {
	void Configure(WebApplication app);
	void ConfigureServices(IServiceCollection services);
}
