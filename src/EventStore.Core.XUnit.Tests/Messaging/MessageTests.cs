// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messages;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Messaging;

public class MessageTests {
	public MessageTests() {
		HttpMessage.TextMessage.LabelStatic = "Http";
	}

	private static void Encode(ICodec codec, string expectedString) {
		var actualString = codec.To(new HttpMessage.TextMessage("Hello World!"));
		Assert.Equal(expectedString, actualString);
	}

	private static void Decode(ICodec codec, string encoded) {
		var m = codec.From<HttpMessage.TextMessage>(encoded);
		Assert.Equal("Hello World!", m.Text);
	}

	private static void RoundTrip(ICodec codec, string expectedString) {
		Encode(codec, expectedString);
		Decode(codec, expectedString);
	}

	[Fact]
	public void can_round_trip_to_json() => RoundTrip(
		Codec.Json,
		"""
		{
		  "text": "Hello World!",
		  "label": "Http"
		}
		""");

	[Fact]
	public void can_encode_to_text() => Encode(
		Codec.Text,
		"Text: Hello World!");

	[Fact]
	public void can_round_trip_to_xml() => RoundTrip(
		Codec.Xml,
		"""
		<?xml version="1.0" encoding="utf-8"?>
		<TextMessage
		 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
		 xmlns:xsd="http://www.w3.org/2001/XMLSchema">
		<Text>Hello World!</Text>
		</TextMessage>
		""".ReplaceLineEndings(""));
}
