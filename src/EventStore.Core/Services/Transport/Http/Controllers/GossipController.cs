// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Client;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using HttpMethod = EventStore.Transport.Http.HttpMethod;
using HttpStatusCode = EventStore.Transport.Http.HttpStatusCode;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class GossipController : CommunicationController {
	private static readonly ILogger Log = Serilog.Log.ForContext<GossipController>();

	private static readonly ICodec[] SupportedCodecs = new ICodec[]
		{Codec.Json, Codec.ApplicationXml, Codec.Xml, Codec.Text};

	private readonly IPublisher _networkSendQueue;
	private readonly IDurationTracker _tracker;

	public GossipController(IPublisher publisher, IPublisher networkSendQueue, IDurationTracker tracker)
		: base(publisher) {
		_networkSendQueue = networkSendQueue;
		_tracker = tracker;
	}

	protected override void SubscribeCore(IHttpService service) {
		service.RegisterAction(new ControllerAction("/gossip", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Gossip.ClientRead)),
			OnGetGossip);
	}

	private void OnGetGossip(HttpEntityManager entity, UriTemplateMatch match) {
		var duration = _tracker.Start();
		var sendToHttpEnvelope = new SendToHttpEnvelope(
			_networkSendQueue, entity, Format.SendPublicGossip,
			(e, m) => {
				duration.Dispose();
				return Configure.Ok(e.ResponseCodec.ContentType, Helper.UTF8NoBom, null, null, false);
			});
		Publish(new GossipMessage.ClientGossip(sendToHttpEnvelope));
	}
}
