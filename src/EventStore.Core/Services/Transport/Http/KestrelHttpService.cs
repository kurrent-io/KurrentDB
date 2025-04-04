// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http.Messages;
using EventStore.Core.Settings;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http;

public class KestrelHttpService : IHttpService,
	IHandle<SystemMessage.SystemInit>,
	IHandle<SystemMessage.BecomeShuttingDown> {
	private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
	private static readonly ILogger Log = Serilog.Log.ForContext<KestrelHttpService>();

	public ServiceAccessibility Accessibility => _accessibility;
	public bool IsListening => _isListening;
	public IEnumerable<EndPoint> EndPoints { get; }

	public IEnumerable<ControllerAction> Actions => UriRouter.Actions;

	private readonly ServiceAccessibility _accessibility;
	private readonly IPublisher _inputBus;
	public IUriRouter UriRouter { get; }
	public bool LogHttpRequests { get; }

	public string AdvertiseAsHost { get; }
	public int AdvertiseAsPort { get; }

	private bool _isListening;

	public KestrelHttpService(ServiceAccessibility accessibility, IPublisher inputBus, IUriRouter uriRouter,
		MultiQueuedHandler multiQueuedHandler, bool logHttpRequests, string advertiseAsHost,
		int advertiseAsPort, bool disableAuthorization, params EndPoint[] endPoints) {
		Ensure.NotNull(inputBus, nameof(inputBus));
		Ensure.NotNull(uriRouter, nameof(uriRouter));
		Ensure.NotNull(endPoints, nameof(endPoints));

		_accessibility = accessibility;
		_inputBus = inputBus;
		UriRouter = uriRouter;
		LogHttpRequests = logHttpRequests;

		AdvertiseAsHost = advertiseAsHost;
		AdvertiseAsPort = advertiseAsPort;


		EndPoints = endPoints;
	}

	public void Handle(SystemMessage.SystemInit message) {

		_isListening = true;
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		if (message.ShutdownHttp)
			Shutdown();
		_inputBus.Publish(
			new SystemMessage.ServiceShutdown(
				$"HttpServer [{String.Join(", ", EndPoints)}]"));
	}

	public void Shutdown() {
		_isListening = false;
	}

	public void SetupController(IHttpController controller) {
		Ensure.NotNull(controller, "controller");
		controller.Subscribe(this);
	}

	public void RegisterCustomAction(ControllerAction action,
		Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler) {
		Ensure.NotNull(action, "action");
		Ensure.NotNull(handler, "handler");

		UriRouter.RegisterAction(action, handler);
	}

	public void RegisterAction(ControllerAction action, Action<HttpEntityManager, UriTemplateMatch> handler) {
		Ensure.NotNull(action, "action");
		Ensure.NotNull(handler, "handler");

		UriRouter.RegisterAction(action, (man, match) => {
			handler(man, match);
			return new RequestParams(ESConsts.HttpTimeout);
		});
	}
	public List<UriToActionMatch> GetAllUriMatches(Uri uri) => UriRouter.GetAllUriMatches(uri);
	public static void CreateAndSubscribePipeline(ISubscriber bus) {
		var requestProcessor = new AuthenticatedHttpRequestProcessor();
		bus.Subscribe<AuthenticatedHttpRequestMessage>(requestProcessor);
	}
}
