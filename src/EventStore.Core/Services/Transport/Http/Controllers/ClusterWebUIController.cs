// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Util;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class ClusterWebUiController : CommunicationController {
	private static readonly ILogger Log = Serilog.Log.ForContext<ClusterWebUiController>();

	private readonly NodeSubsystems[] _enabledNodeSubsystems;

	//private readonly MiniWeb _commonWeb;
	private readonly MiniWeb _clusterNodeWeb;

	public ClusterWebUiController(IPublisher publisher, NodeSubsystems[] enabledNodeSubsystems)
		: base(publisher) {
		_enabledNodeSubsystems = enabledNodeSubsystems;
		_clusterNodeWeb = new MiniWeb("/web");
	}

	protected override void SubscribeCore(IHttpService service) {
		_clusterNodeWeb.RegisterControllerActions(service);
		RegisterRedirectAction(service, "", "/ui/cluster");
		RegisterRedirectAction(service, "/web", "/web/index.html");

		service.RegisterAction(
			new ControllerAction("/sys/subsystems", HttpMethod.Get, Codec.NoCodecs, new ICodec[] {Codec.Json}, new Operation(Operations.Node.Information.Subsystems)),
			OnListNodeSubsystems);
	}

	private void OnListNodeSubsystems(HttpEntityManager http, UriTemplateMatch match) {
		http.ReplyTextContent(
			Codec.Json.To(_enabledNodeSubsystems),
			200,
			"OK",
			ContentType.Json,
			null,
			ex => Log.Information(ex, "Failed to prepare main menu")
		);
	}

	private static void RegisterRedirectAction(IHttpService service, string fromUrl, string toUrl) {
		service.RegisterAction(
			new ControllerAction(
				fromUrl,
				HttpMethod.Get,
				Codec.NoCodecs,
				new ICodec[] {Codec.ManualEncoding},
				new Operation(Operations.Node.Redirect)),
			(http, match) => http.ReplyTextContent(
				"Moved", 302, "Found", ContentType.PlainText,
				new[] {
					new KeyValuePair<string, string>(
						"Location", new Uri(http.HttpEntity.RequestedUrl, toUrl).AbsoluteUri)
				}, Console.WriteLine));
	}
}
