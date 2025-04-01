// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Auth.StreamPolicyPlugin;
using EventStore.Common.Exceptions;
using EventStore.Core;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Core.Configuration.Sources;
using EventStore.PluginHosting;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace KurrentDB;

public class AuthorizationPolicyRegistryFactory: SubsystemsPlugin {
	private readonly ILogger _logger = Log.ForContext<AuthorizationPolicyRegistryFactory>();
	private readonly IPolicySelectorFactory[] _pluginSelectorFactories = [];
	private readonly Func<IPublisher, IAuthorizationPolicyRegistry> _createRegistry;
	private IAuthorizationPolicyRegistry? _authorizationPolicyRegistry;

	public AuthorizationPolicyRegistryFactory(ClusterVNodeOptions options, IConfiguration configuration, PluginLoader pluginLoader) {
		if (options.Application.Insecure) {
			_createRegistry = _ => new StaticAuthorizationPolicyRegistry([]);
			return;
		}

		// Load up all policy selectors in the plugins directory
		var factories = pluginLoader.Load<IPolicySelectorFactory>().ToList();
		factories.Add(new StreamPolicySelectorFactory());

		_pluginSelectorFactories = factories?
			.Select(x => {
				_logger.Information("Loaded Authorization Policy plugin: {plugin}.", x.CommandLineName);
				return x;
			}).ToArray() ?? [];

		// Set up the legacy policy selector factory
		var allowAnonymousEndpointAccess = options.Application.AllowAnonymousEndpointAccess;
		var allowAnonymousStreamAccess = options.Application.AllowAnonymousStreamAccess;
		var overrideAnonymousGossipEndpointAccess = options.Application.OverrideAnonymousEndpointAccessForGossip;
		var legacyPolicyFactory = new LegacyPolicySelectorFactory(
			allowAnonymousEndpointAccess,
			allowAnonymousStreamAccess,
			overrideAnonymousGossipEndpointAccess);

		// Check if there is a default policy type. Use this if the settings stream is empty
		var defaultPolicyType =
			configuration.GetValue<string>($"{KurrentConfigurationKeys.Prefix}:Authorization:DefaultPolicyType") ?? string.Empty;
		AuthorizationPolicySettings defaultSettings;
		if (!string.IsNullOrEmpty(defaultPolicyType)) {
			if (_pluginSelectorFactories.Any(x => x.CommandLineName == defaultPolicyType)) {
				defaultSettings = new AuthorizationPolicySettings(defaultPolicyType);
			} else {
				throw new InvalidConfigurationException(
					$"No authorization policy with the name '{defaultPolicyType}' has been registered. " +
					$"Available authorization policies are: {string.Join(", ", _pluginSelectorFactories.Select(x => x.CommandLineName))}");
			}
		} else {
			// No default policy type configured. Use ACLs.
			defaultSettings = new AuthorizationPolicySettings(LegacyPolicySelectorFactory.LegacyPolicySelectorName);
		}

		// There are no plugins and a default policy type wasn't configured.
		// Don't use the stream based registry
		if (_pluginSelectorFactories.Length == 0) {
			_logger.Information("No authorization policy plugins found. Only ACLs will be used.");
			_createRegistry = publisher => new StaticAuthorizationPolicyRegistry([legacyPolicyFactory.Create(publisher)]);
			return;
		}

		_logger.Information("The default authorization policy settings are: {settings}", defaultSettings);
		_createRegistry = publisher => new StreamBasedAuthorizationPolicyRegistry(publisher, legacyPolicyFactory.Create(publisher), _pluginSelectorFactories, defaultSettings);
	}

	// Use a factory rather than ConfigureApplication because the authorization providers
	// are built (and requires this registry) before ConfigureApplication is called
	public IAuthorizationPolicyRegistry Create(IPublisher publisher) {
		if (_authorizationPolicyRegistry is not null) {
			return _authorizationPolicyRegistry;
		}
		_authorizationPolicyRegistry = _createRegistry(publisher);
		return _authorizationPolicyRegistry;
	}

	public override IReadOnlyList<ISubsystem> GetSubsystems() {
		var subsystems = new List<ISubsystem> { this };
		// ReSharper disable once SuspiciousTypeConversion.Global
		subsystems.AddRange(_pluginSelectorFactories.OfType<ISubsystem>());
		return subsystems.ToArray();
	}

	public override Task Start() {
		return _authorizationPolicyRegistry!.Start();
	}

	public override Task Stop() {
		return _authorizationPolicyRegistry!.Stop();
	}
}
