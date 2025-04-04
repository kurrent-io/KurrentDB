using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Configuration;
using EventStore.Common.Exceptions;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Authentication.PassthroughAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Bus;
using EventStore.Core.Certificates;
using EventStore.Core.Hashing;
using EventStore.Core.LogAbstraction;
using EventStore.Core.PluginModel;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.PluginHosting;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.MD5;
using EventStore.Plugins.Subsystems;
using EventStore.Projections.Core;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EventStore.ClusterNode {
	internal class ClusterVNodeHostedService : IHostedService, IDisposable {
		private static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNodeHostedService>();

		private readonly ClusterVNodeOptions _options;
		private readonly ExclusiveDbLock _dbLock;
		private readonly ClusterNodeMutex _clusterNodeMutex;

		public ClusterVNode Node { get; }

		public ClusterVNodeHostedService(
			ClusterVNodeOptions options,
			CertificateProvider certificateProvider,
			MetricsConfiguration metricsConfiguration) {

			if (options == null)
				throw new ArgumentNullException(nameof(options));

			// two plugin mechanisms; pluginLoader is the new one
			var pluginLoader = new PluginLoader(new DirectoryInfo(Locations.PluginsDirectory));
			var plugInContainer = FindPlugins();

			options = LoadSubsystemsPlugins(pluginLoader, options);

			try {
				ConfigureMD5();
			} catch {
				throw new
					InvalidConfigurationException(
						"Failed to configure MD5. If FIPS mode is enabled in your OS, please use the MD5 commercial plugin.");
			}

			var projectionMode = options.DevMode.Dev && options.Projections.RunProjections == ProjectionType.None
				? ProjectionType.System
				: options.Projections.RunProjections;
			var startStandardProjections = options.Projections.StartStandardProjections || options.DevMode.Dev;
			_options = projectionMode >= ProjectionType.System
				? options.WithSubsystem(new ProjectionsSubsystem(
					new ProjectionSubsystemOptions(
						options.Projections.ProjectionThreads,
						projectionMode,
						startStandardProjections,
						TimeSpan.FromMinutes(options.Projections.ProjectionsQueryExpiry),
						options.Projections.FaultOutOfOrderProjections,
						options.Projections.ProjectionCompilationTimeout,
						options.Projections.ProjectionExecutionTimeout)))
				: options;

			if (!_options.Database.MemDb) {
				var absolutePath = Path.GetFullPath(_options.Database.Db);
				if (Runtime.IsWindows)
					absolutePath = absolutePath.ToLower();

				_dbLock = new ExclusiveDbLock(absolutePath);
				if (!_dbLock.Acquire())
					throw new InvalidConfigurationException($"Couldn't acquire exclusive lock on DB at '{_options.Database.Db}'.");
			}

			_clusterNodeMutex = new ClusterNodeMutex();
			if (!_clusterNodeMutex.Acquire())
				throw new InvalidConfigurationException($"Couldn't acquire exclusive Cluster Node mutex '{_clusterNodeMutex.MutexName}'.");

			var authorizationConfig = string.IsNullOrEmpty(_options.Auth.AuthorizationConfig)
				? _options.Application.Config
				: _options.Auth.AuthorizationConfig;

			var authenticationConfig = string.IsNullOrEmpty(_options.Auth.AuthenticationConfig)
				? _options.Application.Config
				: _options.Auth.AuthenticationConfig;

			if (_options.Database.DbLogFormat == DbLogFormat.V2) {
				var logFormatFactory = new LogV2FormatAbstractorFactory();
				Node = ClusterVNode.Create(_options, logFormatFactory, GetAuthenticationProviderFactory(),
					GetAuthorizationProviderFactory(), GetPersistentSubscriptionConsumerStrategyFactories(), certificateProvider,
					metricsConfiguration);
			} else if (_options.Database.DbLogFormat == DbLogFormat.ExperimentalV3) {
				var logFormatFactory = new LogV3FormatAbstractorFactory();
				Node = ClusterVNode.Create(_options, logFormatFactory, GetAuthenticationProviderFactory(),
					GetAuthorizationProviderFactory(), GetPersistentSubscriptionConsumerStrategyFactories(), certificateProvider,
					metricsConfiguration);
			} else {
				throw new ArgumentOutOfRangeException(nameof(_options.Database.DbLogFormat), "Unexpected log format specified.");
			}

			var enabledNodeSubsystems = projectionMode >= ProjectionType.System
				? new[] { NodeSubsystems.Projections }
				: Array.Empty<NodeSubsystems>();

			RegisterWebControllers(enabledNodeSubsystems);

			AuthorizationProviderFactory GetAuthorizationProviderFactory() {
				if (_options.Application.Insecure) {
					return new AuthorizationProviderFactory(_ => new PassthroughAuthorizationProviderFactory());
				}

				var authorizationTypeToPlugin = new Dictionary<string, AuthorizationProviderFactory> {
					{
						"internal", new AuthorizationProviderFactory(components =>
							new LegacyAuthorizationProviderFactory(components.MainQueue,
								_options.Application.AllowAnonymousEndpointAccess,
								_options.Application.AllowAnonymousStreamAccess,
								_options.Application.OverrideAnonymousEndpointAccessForGossip))
					}
				};

				foreach (var potentialPlugin in pluginLoader.Load<IAuthorizationPlugin>()) {
					try {
						var commandLine = potentialPlugin.CommandLineName.ToLowerInvariant();
						Log.Information(
							"Loaded authorization plugin: {plugin} version {version} (Command Line: {commandLine})",
							potentialPlugin.Name, potentialPlugin.Version, commandLine);
						authorizationTypeToPlugin.Add(commandLine,
							new AuthorizationProviderFactory(_ =>
								potentialPlugin.GetAuthorizationProviderFactory(authorizationConfig)));
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

				return factory;
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

				foreach (var potentialPlugin in pluginLoader.Load<IAuthenticationPlugin>()) {
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
				var plugins = pluginLoader.Load<ISubsystemsPlugin<(ISubscriber, IPublisher)>>().ToArray();
				foreach (var plugin in plugins) {
					Log.Information("Loaded SubsystemsPlugin plugin: {plugin} {version}.",
						plugin.CommandLineName,
						plugin.Version);
					var subsystemFactories = plugin.GetSubsystemFactories(configPath: options.Application.Config);
					foreach (var subsystemFactory in subsystemFactories) {
						options = options.WithSubsystem(new SubsystemFactoryAdapter<(ISubscriber, IPublisher)>(
							subsystemFactory,
							components => (components.MainBus, components.MainQueue)));
					}
				}
				return options;
			}

			void ConfigureMD5() {
				var md5Provider = GetMD5ProviderFactories().FirstOrDefault()?.Build();
				MD5.UseProvider(md5Provider ?? new NetMD5Provider());
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
			_options.Application.WhatIf ? Task.CompletedTask : Node.StartAsync(false);

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
}
