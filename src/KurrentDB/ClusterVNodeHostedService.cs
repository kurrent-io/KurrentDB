// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.MD5;
using EventStore.Plugins.Subsystems;
using KurrentDB.Auth.Ldaps;
using KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;
using KurrentDB.Auth.OAuth;
using KurrentDB.Auth.UserCertificates;
using KurrentDB.AutoScavenge;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Options;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Authentication;
using KurrentDB.Core.Authentication.InternalAuthentication;
using KurrentDB.Core.Authentication.PassthroughAuthentication;
using KurrentDB.Core.Authorization;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Hashing;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.PluginModel;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Diagnostics.LogsEndpointPlugin;
using KurrentDB.PluginHosting;
using KurrentDB.Plugins.Connectors;
using KurrentDB.Plugins.SchemaRegistry;
using KurrentDB.POC.ConnectedSubsystemsPlugin;
using KurrentDB.Projections.Core;
using KurrentDB.SecondaryIndexing;
using KurrentDB.Security.EncryptionAtRest;
using KurrentDB.TcpPlugin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using LogV3StreamId = System.UInt32;

namespace KurrentDB;

public class ClusterVNodeHostedService : IHostedService, IDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNodeHostedService>();

	private readonly ClusterVNodeOptions _options;
	private readonly ExclusiveDbLock _dbLock;

	public ClusterVNode Node { get; }

	public ClusterVNodeHostedService(
		ClusterVNodeOptions options,
		CertificateProvider certificateProvider,
		IConfiguration configuration) {

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		// two plugin mechanisms; pluginLoader is the new one
		var pluginLoader = new PluginLoader(new DirectoryInfo(Locations.PluginsDirectory));
		var plugInContainer = FindPlugins();

		options = LoadSubsystemsPlugins(pluginLoader, options);

		try {
			options = options.WithPlugableComponent(ConfigureMD5());
		} catch {
			throw new
				InvalidConfigurationException(
					"Failed to configure MD5. If FIPS mode is enabled in your OS, please use the MD5 commercial plugin.");
		}

		var projectionMode = options.DevMode.Dev && options.Projection.RunProjections == ProjectionType.None
			? ProjectionType.System
			: options.Projection.RunProjections;
		var startStandardProjections = options.Projection.StartStandardProjections || options.DevMode.Dev;
		_options = projectionMode >= ProjectionType.System
			? options.WithPlugableComponent(new ProjectionsSubsystem(
				new ProjectionSubsystemOptions(
					options.Projection.ProjectionThreads,
					projectionMode,
					startStandardProjections,
					TimeSpan.FromMinutes(options.Projection.ProjectionsQueryExpiry),
					options.Projection.FaultOutOfOrderProjections,
					options.Projection.ProjectionCompilationTimeout,
					options.Projection.ProjectionExecutionTimeout,
					options.Projection.MaxProjectionStateSize) , options.InMemoryBus.SlowMessageThresholdMs))
			: options;

		if (!_options.Database.MemDb) {
			var absolutePath = Path.GetFullPath(_options.Database.Db);
			if (RuntimeInformation.IsWindows)
				absolutePath = absolutePath.ToLower();

			_dbLock = new ExclusiveDbLock(absolutePath);
			if (!_dbLock.Acquire())
				throw new InvalidConfigurationException($"Couldn't acquire exclusive lock on DB at '{_options.Database.Db}'.");
		}
		var authorizationConfig = string.IsNullOrEmpty(_options.Auth.AuthorizationConfig)
			? _options.Application.Config
			: _options.Auth.AuthorizationConfig;

		var authenticationConfig = string.IsNullOrEmpty(_options.Auth.AuthenticationConfig)
			? _options.Application.Config
			: _options.Auth.AuthenticationConfig;


		(_options, var authProviderFactory) = GetAuthorizationProviderFactory();

		var virtualStreamReader = new VirtualStreamReader();

		if (_options.Database.DbLogFormat == DbLogFormat.V2) {
			var secondaryIndexingPlugin = SecondaryIndexingPluginFactory.Create<string>(virtualStreamReader);
			_options = _options.WithPlugableComponents(secondaryIndexingPlugin);

			var logFormatFactory = new LogV2FormatAbstractorFactory();
			var node = ClusterVNode.Create(_options, logFormatFactory, GetAuthenticationProviderFactory(),
				authProviderFactory,
				virtualStreamReader,
				GetPersistentSubscriptionConsumerStrategyFactories(), certificateProvider,
				configuration);
			Node = node;
		} else if (_options.Database.DbLogFormat == DbLogFormat.ExperimentalV3) {
			var secondaryIndexingPlugin = SecondaryIndexingPluginFactory.Create<LogV3StreamId>(virtualStreamReader);
			_options = _options.WithPlugableComponents(secondaryIndexingPlugin);

			var logFormatFactory = new LogV3FormatAbstractorFactory();
			var node = ClusterVNode.Create(_options, logFormatFactory, GetAuthenticationProviderFactory(),
				authProviderFactory,
				virtualStreamReader,
				GetPersistentSubscriptionConsumerStrategyFactories(), certificateProvider,
				configuration);
			Node = node;
		} else {
			throw new ArgumentOutOfRangeException(nameof(_options.Database.DbLogFormat), "Unexpected log format specified.");
		}

		var enabledNodeSubsystems = projectionMode >= ProjectionType.System
			? new[] { NodeSubsystems.Projections }
			: Array.Empty<NodeSubsystems>();

		RegisterWebControllers(enabledNodeSubsystems);
		return;

		(ClusterVNodeOptions, AuthorizationProviderFactory) GetAuthorizationProviderFactory() {
			if (_options.Application.Insecure) {
				return (_options, new AuthorizationProviderFactory(_ => new PassthroughAuthorizationProviderFactory()));
			}

			var modifiedOptions = _options;
			if (_options.Auth.AuthorizationType.Equals("internal", StringComparison.InvariantCultureIgnoreCase)) {
				var registryFactory = new AuthorizationPolicyRegistryFactory(_options, configuration, pluginLoader);
				foreach (var authSubsystem in registryFactory.GetSubsystems()) {
					modifiedOptions = modifiedOptions.WithPlugableComponent(authSubsystem);
				}

				var internalFactory = new AuthorizationProviderFactory(components =>
					new InternalAuthorizationProviderFactory(registryFactory.Create(components.MainQueue)));
				return (modifiedOptions, internalFactory);
			}

			var authorizationTypeToPlugin = new Dictionary<string, AuthorizationProviderFactory> { };
			var authzPlugins = pluginLoader.Load<IAuthorizationPlugin>().ToList();
			authzPlugins.Add(new LegacyAuthorizationWithStreamAuthorizationDisabledPlugin());

			foreach (var potentialPlugin in authzPlugins) {
				try {
					var commandLine = potentialPlugin.CommandLineName.ToLowerInvariant();
					Log.Information(
						"Loaded authorization plugin: {plugin} version {version} (Command Line: {commandLine})",
						potentialPlugin.Name, potentialPlugin.Version, commandLine);
					authorizationTypeToPlugin.Add(commandLine,
						new AuthorizationProviderFactory(
							_ => potentialPlugin.GetAuthorizationProviderFactory(authorizationConfig)
						));
				} catch (CompositionException ex) {
					Log.Error(ex, "Error loading authentication plugin.");
				}
			}

			if (!authorizationTypeToPlugin.TryGetValue(_options.Auth.AuthorizationType.ToLowerInvariant(),
				out var factory)) {
				throw new ApplicationInitializationException(
					$"The authorization type {_options.Auth.AuthorizationType} is not recognised. If this is supposed " +
					$"to be provided by an authorization plugin, confirm the plugin DLL is located in {Locations.PluginsDirectory}." +
					Environment.NewLine +
					$"Valid options for authorization are: {string.Join(", ", authorizationTypeToPlugin.Keys)}.");
			}

			return (modifiedOptions, factory);
		}

		static CompositionContainer FindPlugins() {
			var catalog = new AggregateCatalog();

			catalog.Catalogs.Add(new AssemblyCatalog(typeof(ClusterVNodeHostedService).Assembly));

			if (Directory.Exists(Locations.PluginsDirectory)) {
				Log.Information("Plugins path: {pluginsDirectory}", Locations.PluginsDirectory);

				Log.Information("Adding: {pluginsDirectory} to the plugin catalog.", Locations.PluginsDirectory);
				catalog.Catalogs.Add(new DirectoryCatalog(Locations.PluginsDirectory));

				foreach (string dirPath in Directory.GetDirectories(Locations.PluginsDirectory, "*",
					SearchOption.TopDirectoryOnly)) {
					Log.Information("Adding: {pluginsDirectory} to the plugin catalog.", dirPath);
					catalog.Catalogs.Add(new DirectoryCatalog(dirPath));
				}
			} else {
				Log.Information("Cannot find plugins path: {pluginsDirectory}", Locations.PluginsDirectory);
			}

			return new CompositionContainer(catalog);
		}

		IPersistentSubscriptionConsumerStrategyFactory[] GetPersistentSubscriptionConsumerStrategyFactories() {
			var allPlugins = plugInContainer.GetExports<IPersistentSubscriptionConsumerStrategyPlugin>();

			var strategyFactories = new List<IPersistentSubscriptionConsumerStrategyFactory>();

			foreach (var potentialPlugin in allPlugins) {
				try {
					var plugin = potentialPlugin.Value;
					Log.Information("Loaded consumer strategy plugin: {plugin} version {version}.", plugin.Name,
						plugin.Version);
					strategyFactories.Add(plugin.GetConsumerStrategyFactory());
				} catch (CompositionException ex) {
					Log.Error(ex, "Error loading consumer strategy plugin.");
				}
			}

			return strategyFactories.ToArray();
		}

		AuthenticationProviderFactory GetAuthenticationProviderFactory() {
			if (_options.Application.Insecure) {
				return new AuthenticationProviderFactory(_ => new PassthroughAuthenticationProviderFactory());
			}

			var authenticationTypeToPlugin = new Dictionary<string, AuthenticationProviderFactory> {
				{
					"internal", new AuthenticationProviderFactory(components =>
						new InternalAuthenticationProviderFactory(components, _options.DefaultUser))
				}
			};

			var authPlugins = pluginLoader.Load<IAuthenticationPlugin>().ToList();
			authPlugins.Add(new LdapsAuthenticationPlugin());
			authPlugins.Add(new OAuthAuthenticationPlugin());

			foreach (var potentialPlugin in authPlugins) {
				try {
					var commandLine = potentialPlugin.CommandLineName.ToLowerInvariant();
					Log.Information(
						"Loaded authentication plugin: {plugin} version {version} (Command Line: {commandLine})",
						potentialPlugin.Name, potentialPlugin.Version, commandLine);
					authenticationTypeToPlugin.Add(commandLine,
						new AuthenticationProviderFactory(_ =>
							potentialPlugin.GetAuthenticationProviderFactory(authenticationConfig)));
				} catch (CompositionException ex) {
					Log.Error(ex, "Error loading authentication plugin.");
				}
			}

			return authenticationTypeToPlugin.TryGetValue(_options.Auth.AuthenticationType.ToLowerInvariant(),
				out var factory)
				? factory
				: throw new ApplicationInitializationException(
					$"The authentication type {_options.Auth.AuthenticationType} is not recognised. If this is supposed " +
					$"to be provided by an authentication plugin, confirm the plugin DLL is located in {Locations.PluginsDirectory}." +
					Environment.NewLine +
					$"Valid options for authentication are: {string.Join(", ", authenticationTypeToPlugin.Keys)}.");
		}

		static ClusterVNodeOptions LoadSubsystemsPlugins(PluginLoader pluginLoader, ClusterVNodeOptions options) {
			var plugins = pluginLoader.Load<ISubsystemsPlugin>().ToList();
			plugins.Add(new OtlpExporterPlugin.OtlpExporterPlugin());
			plugins.Add(new UserCertificatesPlugin());
			plugins.Add(new LogsEndpointPlugin());
			plugins.Add(new EncryptionAtRestPlugin());
			plugins.Add(new ConnectedSubsystemsPlugin());
			plugins.Add(new AutoScavengePlugin());
			plugins.Add(new TcpApiPlugin());
			plugins.Add(new ConnectorsPlugin());
			plugins.Add(new SchemaRegistryPlugin());

			foreach (var plugin in plugins) {
				Log.Information("Loaded SubsystemsPlugin plugin: {plugin} {version}.",
					plugin.CommandLineName,
					plugin.Version);
				var subsystems = plugin.GetSubsystems();
				foreach (var subsystem in subsystems) {
					options = options.WithPlugableComponent(subsystem);
				}
			}
			return options;
		}

		IPlugableComponent ConfigureMD5() {
			IMD5Provider provider;
			try {
				// use the default net md5 provider if we can - i.e. in non fips environments.
				provider = new NetMD5Provider();
				MD5.UseProvider(provider);
			} catch {
				// didn't work, we are probably in a fips environment, try to load a plugin
				provider = GetMD5ProviderFactories().FirstOrDefault()?.Build() ??
					throw new ApplicationInitializationException("Could not find an enabled FileHashProviderFactory");
				MD5.UseProvider(provider);
			}

			Log.Information("Using {Name} FileHashProvider.", provider.Name);
			return provider;
		}

		IEnumerable<IMD5ProviderFactory> GetMD5ProviderFactories() {
			var md5ProviderFactories = new List<IMD5ProviderFactory>();

			foreach (var plugin in pluginLoader.Load<IMD5Plugin>()) {
				try {
					var commandLine = plugin.CommandLineName.ToLowerInvariant();
					Log.Information(
						"Loaded MD5 plugin: {plugin} version {version} (Command Line: {commandLine})",
						plugin.Name, plugin.Version, commandLine);
					md5ProviderFactories.Add(plugin.GetMD5ProviderFactory());
				} catch (CompositionException ex) {
					Log.Error(ex, "Error loading MD5 plugin: {plugin}.", plugin.Name);
				}
			}

			return md5ProviderFactories.ToArray();
		}
	}

	private void RegisterWebControllers(NodeSubsystems[] enabledNodeSubsystems) {
		if (!_options.Interface.DisableAdminUi) {
			Node.HttpService.SetupController(new ClusterWebUiController(Node.MainQueue,
				enabledNodeSubsystems));
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) =>
		_options.Application.WhatIf ? Task.CompletedTask : Node.StartAsync(waitUntilReady: false, cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken) =>
		Node.StopAsync(cancellationToken: cancellationToken);

	public void Dispose() {
		if (_dbLock is not { IsAcquired: true }) {
			return;
		}
		using (_dbLock) {
			_dbLock.Release();
		}
	}
}
