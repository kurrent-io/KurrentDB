// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Transport.Http;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http;

public class SendToHttpWithConversionEnvelope<TExpectedResponseMessage, TExpectedHttpFormattedResponseMessage> : IEnvelope
	where TExpectedResponseMessage : Message {
	private readonly Func<ICodec, TExpectedHttpFormattedResponseMessage, string> _formatter;
	private readonly Func<ICodec, TExpectedHttpFormattedResponseMessage, ResponseConfiguration> _configurator;
	private readonly Func<TExpectedResponseMessage, TExpectedHttpFormattedResponseMessage> _convertor;

	private readonly SendToHttpEnvelope<TExpectedResponseMessage> _httpEnvelope;

	public SendToHttpWithConversionEnvelope(IPublisher networkSendQueue,
		HttpEntityManager entity,
		Func<ICodec, TExpectedHttpFormattedResponseMessage, string> formatter,
		Func<ICodec, TExpectedHttpFormattedResponseMessage, ResponseConfiguration> configurator,
		Func<TExpectedResponseMessage, TExpectedHttpFormattedResponseMessage> convertor,
		IEnvelope nonMatchingEnvelope = null) {
		_formatter = formatter;
		_configurator = configurator;
		_convertor = convertor;
		_httpEnvelope = new(networkSendQueue, entity, Formatter, Configurator, nonMatchingEnvelope);
	}

	private ResponseConfiguration Configurator(ICodec codec, TExpectedResponseMessage message) {
		var convertedMessage = _convertor(message);
		return _configurator(codec, convertedMessage);
	}

	private string Formatter(ICodec codec, TExpectedResponseMessage message) {
		var convertedMessage = _convertor(message);
		return _formatter(codec, convertedMessage);
	}

	public void ReplyWith<T>(T message) where T : Message {
		_httpEnvelope.ReplyWith(message);
	}
}
