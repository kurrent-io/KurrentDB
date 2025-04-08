// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Transport.Http;
using KurrentDB.Transport.Http.EntityManagement;

namespace KurrentDB.Core.Services.Transport.Http;

public class HttpResponseConfiguratorArgs(Uri responseUrl, Uri requestedUrl, ICodec responseCodec) {
	public readonly Uri ResponseUrl = responseUrl;
	public readonly Uri RequestedUrl = requestedUrl;
	public readonly ICodec ResponseCodec = responseCodec;

	public static implicit operator HttpResponseConfiguratorArgs(HttpEntityManager entity) {
		return new(entity.ResponseUrl, entity.RequestedUrl, entity.ResponseCodec);
	}
}

public class HttpResponseFormatterArgs(Uri responseUrl, Uri requestedUrl, ICodec responseCodec) {
	public readonly Uri ResponseUrl = responseUrl;
	public readonly Uri RequestedUrl = requestedUrl;
	public readonly ICodec ResponseCodec = responseCodec;

	public static implicit operator HttpResponseFormatterArgs(HttpEntityManager entity) {
		return new(entity.ResponseUrl, entity.RequestedUrl, entity.ResponseCodec);
	}
}

public class SendToHttpEnvelope : IEnvelope {
	private readonly IPublisher _networkSendQueue;
	private readonly HttpEntityManager _entity;
	private readonly Func<HttpResponseFormatterArgs, Message, object> _formatter;
	private readonly Func<HttpResponseConfiguratorArgs, Message, ResponseConfiguration> _configurator;

	public SendToHttpEnvelope(
		IPublisher networkSendQueue,
		HttpEntityManager entity,
		Func<HttpResponseFormatterArgs, Message, object> formatter,
		Func<HttpResponseConfiguratorArgs, Message, ResponseConfiguration> configurator) {

		_networkSendQueue = Ensure.NotNull(networkSendQueue);
		_entity = Ensure.NotNull(entity);
		_formatter = Ensure.NotNull(formatter);
		_configurator = Ensure.NotNull(configurator);
	}

	public void ReplyWith<T>(T message) where T : Message {
		var responseConfiguration = _configurator(_entity, Ensure.NotNull(message));
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
			return new(500, "Internal server error", ContentType.PlainText, Helper.UTF8NoBom);
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
