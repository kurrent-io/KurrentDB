// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using KurrentDB.Core.Messages;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Http.Messages;
using KurrentDB.Core.Settings;
using KurrentDB.Transport.Http.EntityManagement;

namespace KurrentDB.Core.Services.Transport.Http;

public class KestrelHttpService :
	IHttpService,
	IHandle<SystemMessage.SystemInit>,
	IHandle<SystemMessage.BecomeShuttingDown> {

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

	public KestrelHttpService(
		ServiceAccessibility accessibility,
		IPublisher inputBus,
		IUriRouter uriRouter,
		bool logHttpRequests,
		string advertiseAsHost,
		int advertiseAsPort,
		params EndPoint[] endPoints) {
		_accessibility = accessibility;
		EndPoints = Ensure.NotNull(endPoints);
		_inputBus = Ensure.NotNull(inputBus);
		UriRouter = Ensure.NotNull(uriRouter);
		LogHttpRequests = logHttpRequests;
		AdvertiseAsHost = advertiseAsHost;
		AdvertiseAsPort = advertiseAsPort;
	}

	public void Handle(SystemMessage.SystemInit message) {
		_isListening = true;
		_inputBus.Publish(new SystemMessage.ServiceInitialized(nameof(KestrelHttpService)));
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		if (message.ShutdownHttp)
			Shutdown();
		_inputBus.Publish(new SystemMessage.ServiceShutdown(nameof(KestrelHttpService), $"[{string.Join(", ", EndPoints)}]"));
	}

	public void Shutdown() {
		_isListening = false;
	}

	public void SetupController(IHttpController controller) {
		Ensure.NotNull(controller);
		controller.Subscribe(this);
	}

	public void RegisterCustomAction(ControllerAction action, Func<HttpEntityManager, UriTemplateMatch, RequestParams> handler) {
		Ensure.NotNull(action);
		Ensure.NotNull(handler);

		UriRouter.RegisterAction(action, handler);
	}

	public void RegisterAction(ControllerAction action, Action<HttpEntityManager, UriTemplateMatch> handler) {
		Ensure.NotNull(action);
		Ensure.NotNull(handler);

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
