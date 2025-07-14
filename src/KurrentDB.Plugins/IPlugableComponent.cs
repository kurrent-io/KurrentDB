// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Plugins;

/// <summary>
///     Component that can be plugged into the main server.
/// </summary>
public interface IPlugableComponent {
	/// <summary>
	///     The name of the component.
	/// </summary>
	string Name { get; }

	/// <summary>
	///     The name used for diagnostics.
	/// </summary>
	string DiagnosticsName { get; }

	/// <summary>
	///     The tags used for diagnostics.
	/// </summary>
	KeyValuePair<string, object?>[] DiagnosticsTags { get; }
	
	/// <summary>
	///     The version of the component.
	/// </summary>
	string Version { get; }

	/// <summary>
	///     Indicates whether the component is enabled.
	/// </summary>
	bool Enabled { get; }

	/// <summary>
	///		The public key used for licensing.
	/// </summary>
	string LicensePublicKey { get; }
	
	/// <summary>
	///     Configures the services using the provided IServiceCollection and IConfiguration.
	/// </summary>
	/// <param name="services">The IServiceCollection to use for configuration.</param>
	/// <param name="configuration">The IConfiguration to use for configuration.</param>
	void ConfigureServices(IServiceCollection services, IConfiguration configuration);

	/// <summary>
	///     Configures the application using the provided WebHostBuilderContext and IApplicationBuilder.
	/// </summary>
	/// <param name="builder">The IApplicationBuilder to use for configuration.</param>
	/// <param name="configuration">The IConfiguration to use for configuration.</param>
	void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration);
}
