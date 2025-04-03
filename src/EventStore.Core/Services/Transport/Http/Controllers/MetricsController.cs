// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class MetricsController : CommunicationController {
	private static readonly ICodec[] SupportedCodecs = new ICodec[] {
		Codec.CreateCustom(Codec.Text, ContentType.PlainText, Helper.UTF8NoBom, false, false),
		Codec.CreateCustom(Codec.Text, ContentType.OpenMetricsText, Helper.UTF8NoBom, false, false),
	};

	public MetricsController() : base(new NoOpPublisher()) {
	}

	protected override void SubscribeCore(IHttpService service) {
		Ensure.NotNull(service, "service");

		// this exists only to specify the permissions required for the /metrics endpoint
		service.RegisterAction(new ControllerAction("/metrics", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Node.Statistics.Read)),
			(x, y) => {
				// the PrometheusExporterMiddleware handles the request itself, this will not be called
				throw new InvalidOperationException();
			});
	}

	class NoOpPublisher : IPublisher {
		public void Publish(Message message) {
		}
	}
}
