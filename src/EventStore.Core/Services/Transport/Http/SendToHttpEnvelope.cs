// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Transport.Http;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http;

public class HttpResponseConfiguratorArgs {
	public readonly Uri ResponseUrl;
	public readonly Uri RequestedUrl;
	public readonly ICodec ResponseCodec;

	public HttpResponseConfiguratorArgs(Uri responseUrl, Uri requestedUrl, ICodec responseCodec) {
		ResponseUrl = responseUrl;
		RequestedUrl = requestedUrl;
		ResponseCodec = responseCodec;
	}

	public static implicit operator HttpResponseConfiguratorArgs(HttpEntityManager entity) {
		return new HttpResponseConfiguratorArgs(entity.ResponseUrl, entity.RequestedUrl, entity.ResponseCodec);
	}
}

public class HttpResponseFormatterArgs {
	public readonly Uri ResponseUrl;
	public readonly Uri RequestedUrl;
	public readonly ICodec ResponseCodec;

	public HttpResponseFormatterArgs(Uri responseUrl, Uri requestedUrl, ICodec responseCodec) {
		ResponseUrl = responseUrl;
		RequestedUrl = requestedUrl;
		ResponseCodec = responseCodec;
	}

	public static implicit operator HttpResponseFormatterArgs(HttpEntityManager entity) {
		return new HttpResponseFormatterArgs(entity.ResponseUrl, entity.RequestedUrl, entity.ResponseCodec);
	}
}

public class SendToHttpEnvelope : IEnvelope {
	private readonly IPublisher _networkSendQueue;
	private readonly HttpEntityManager _entity;
	private readonly Func<HttpResponseFormatterArgs, Message, object> _formatter;
	private readonly Func<HttpResponseConfiguratorArgs, Message, ResponseConfiguration> _configurator;

	public SendToHttpEnvelope(IPublisher networkSendQueue,
		HttpEntityManager entity,
		Func<HttpResponseFormatterArgs, Message, object> formatter,
		Func<HttpResponseConfiguratorArgs, Message, ResponseConfiguration> configurator) {
		Ensure.NotNull(networkSendQueue, "networkSendQueue");
		Ensure.NotNull(entity, "entity");
		Ensure.NotNull(formatter, "formatter");
		Ensure.NotNull(configurator, "configurator");

		_networkSendQueue = networkSendQueue;
		_entity = entity;
		_formatter = formatter;
		_configurator = configurator;
	}

	public void ReplyWith<T>(T message) where T : Message {
		Ensure.NotNull(message, "message");
		var responseConfiguration = _configurator(_entity, message);
		var data = _formatter(_entity, message);
		_networkSendQueue.Publish(new HttpMessage.HttpSend(_entity, responseConfiguration, data, message));
	}
}

public class SendToHttpEnvelope<TExpectedResponseMessage> : IEnvelope where TExpectedResponseMessage : Message {
	private readonly Func<ICodec, TExpectedResponseMessage, string> _formatter;
	private readonly Func<ICodec, TExpectedResponseMessage, ResponseConfiguration> _configurator;
	private readonly IEnvelope _notMatchingEnvelope;

	private readonly IEnvelope _httpEnvelope;

	public SendToHttpEnvelope(IPublisher networkSendQueue,
		HttpEntityManager entity,
		Func<ICodec, TExpectedResponseMessage, string> formatter,
		Func<ICodec, TExpectedResponseMessage, ResponseConfiguration> configurator,
		IEnvelope notMatchingEnvelope) {
		_formatter = formatter;
		_configurator = configurator;
		_notMatchingEnvelope = notMatchingEnvelope;
		_httpEnvelope = new SendToHttpEnvelope(networkSendQueue, entity, Formatter, Configurator);
	}

	private ResponseConfiguration Configurator(HttpResponseConfiguratorArgs http, Message message) {
		try {
			return _configurator(http.ResponseCodec, (TExpectedResponseMessage)message);
		} catch (InvalidCastException) {
			//NOTE: using exceptions to allow handling errors in debugger
			return new ResponseConfiguration(500, "Internal server error", "text/plain", Helper.UTF8NoBom);
		}
	}

	private string Formatter(HttpResponseFormatterArgs http, Message message) {
		try {
			return _formatter(http.ResponseCodec, (TExpectedResponseMessage)message);
		} catch (InvalidCastException) {
			//NOTE: using exceptions to allow handling errors in debugger
			return "";
		}
	}

	public void ReplyWith<T>(T message) where T : Message {
		if (message is TExpectedResponseMessage || _notMatchingEnvelope == null)
			_httpEnvelope.ReplyWith(message);
		else
			_notMatchingEnvelope.ReplyWith(message);
	}
}
