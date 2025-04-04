// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventStore.Auth.UserCertificates;

public class UserCertificatesPlugin : SubsystemsPlugin {
	public const string KurrentConfigurationPrefix = "KurrentDB";
	private static readonly ILogger _staticLogger = Log.ForContext<UserCertificatesPlugin>();
	private readonly ILogger _logger;

	private const string AnonymousProvider = "anonymous";

	public UserCertificatesPlugin() : this(_staticLogger) {
	}

	public UserCertificatesPlugin(ILogger logger)
		: base(
			requiredEntitlements: ["USER_CERTIFICATES"]) {

		_logger = logger;
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{KurrentConfigurationPrefix}:UserCertificates:Enabled", defaultValue: false);
		return (enabled, $"Set '{KurrentConfigurationPrefix}:UserCertificates:Enabled' to 'true' to enable user X.509 certificate authentication");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services
			.AddSingleton<UserCertificateAuthenticationProvider>()
			.Decorate<IReadOnlyList<IHttpAuthenticationProvider>>(Decorate);
	}

	// inject the user certificate authentication provider just before the anonymous provider -
	// its order w.r.t the node certificate authentication provider is unimportant because they accept disjoint sets of certificates
	private IReadOnlyList<IHttpAuthenticationProvider> Decorate(
		IReadOnlyList<IHttpAuthenticationProvider> authProviders,
		IServiceProvider serviceProvider) {

		var authenticationProvider = serviceProvider.GetRequiredService<IAuthenticationProvider>();
		if (!authenticationProvider.GetSupportedAuthenticationSchemes().Contains("UserCertificate")) {
			_logger.Information($"{nameof(UserCertificatesPlugin)} is not compatible with the current authentication provider");
			return authProviders;
		}

		var newAuthProviders = authProviders.ToList();
		var anonymousProviderIndex = newAuthProviders.FindIndex(x => x.Name == AnonymousProvider);

		if (anonymousProviderIndex == -1) {
			_logger.Error($"{nameof(UserCertificatesPlugin)} failed to load as the conditions required to load the plugin were not met");
			return authProviders;
		}

		var userCertAuthProvider = serviceProvider.GetService<UserCertificateAuthenticationProvider>();
		newAuthProviders.Insert(anonymousProviderIndex, userCertAuthProvider);

		_logger.Information($"{nameof(UserCertificatesPlugin)}: user X.509 certificate authentication is enabled");
		return newAuthProviders;
	}
}
