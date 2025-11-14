// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authentication;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Transport.Http.Authentication;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Core.Settings;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;

namespace KurrentDB.Core.Authentication.InternalAuthentication;

public class InternalAuthenticationProviderFactory : IAuthenticationProviderFactory {
	private readonly AuthenticationProviderFactoryComponents _components;
	private readonly IODispatcher _dispatcher;
	private readonly Rfc2898PasswordHashAlgorithm _passwordHashAlgorithm;
	private readonly ClusterVNodeOptions.DefaultUserOptions _defaultUserOptions;

	public InternalAuthenticationProviderFactory(AuthenticationProviderFactoryComponents components, ClusterVNodeOptions.DefaultUserOptions defaultUserOptions) {
		_components = components;
		_passwordHashAlgorithm = new();
		_dispatcher = new(components.MainQueue, components.WorkersQueue);
		_defaultUserOptions = defaultUserOptions;

		components.WorkerBus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(_dispatcher.ForwardReader);
		components.WorkerBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_dispatcher.BackwardReader);
		components.WorkerBus.Subscribe<ClientMessage.NotHandled>(_dispatcher.BackwardReader);
		components.WorkerBus.Subscribe<ClientMessage.WriteEventsCompleted>(_dispatcher.Writer);
		components.WorkerBus.Subscribe<ClientMessage.DeleteStreamCompleted>(_dispatcher.StreamDeleter);
		components.WorkerBus.Subscribe<IODispatcherDelayedMessage>(_dispatcher.Awaker);
		components.WorkerBus.Subscribe<IODispatcherDelayedMessage>(_dispatcher);
		components.WorkerBus.Subscribe<ClientMessage.NotHandled>(_dispatcher);

		var usersController = new UsersController(
			components.HttpSendService,
			components.MainQueue,
			components.WorkersQueue
		);

		components.HttpService.SetupController(usersController);
	}

	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) {
		var provider = new InternalAuthenticationProvider(
			subscriber: _components.MainBus,
			ioDispatcher: _dispatcher,
			passwordHashAlgorithm: _passwordHashAlgorithm,
			cacheSize: ESConsts.CachedPrincipalCount,
			logFailedAuthenticationAttempts: logFailedAuthenticationAttempts,
			defaultUserOptions: _defaultUserOptions
		);

		var passwordChangeNotificationReader = new PasswordChangeNotificationReader(_components.MainQueue, _dispatcher);
		_components.MainBus.Subscribe<SystemMessage.SystemStart>(passwordChangeNotificationReader);
		_components.MainBus.Subscribe<SystemMessage.BecomeShutdown>(passwordChangeNotificationReader);
		_components.MainBus.Subscribe(provider);

		return provider;
	}
}
