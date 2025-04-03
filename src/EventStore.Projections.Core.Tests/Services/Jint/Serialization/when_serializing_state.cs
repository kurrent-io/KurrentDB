// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using EventStore.Projections.Core.Services.Interpreted;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Native.Object;
using NUnit.Framework;
using JsonSerializer = Jint.Native.Json.JsonSerializer;

namespace EventStore.Projections.Core.Tests.Services.Jint.Serialization;

[TestFixture]
public class when_serializing_state {
	private readonly Engine _engine;
	private readonly JintProjectionStateHandler _sut;
	private readonly JsonSerializer _builtIn;
	private readonly JsonParser _parser;

	public when_serializing_state() {
		_engine = new Engine();
		_parser = new JsonParser(_engine);
		_builtIn = new JsonSerializer(_engine);
		_sut = new JintProjectionStateHandler(
			source: "",
			enableContentTypeValidation: false,
			compilationTimeout: TimeSpan.FromMilliseconds(500),
			executionTimeout: TimeSpan.FromMilliseconds(500));
	}

	private void RoundTrip(string json, bool ignoreCase = false) {
		var instance = _parser.Parse(json);
		var builtInSerialized = _builtIn.Serialize(instance, JsValue.Undefined, JsValue.Undefined).AsString();
		var serialized = _sut.Serialize(instance);

		if (ignoreCase) {
			Assert.IsTrue(
				string.Equals(builtInSerialized, serialized, StringComparison.OrdinalIgnoreCase),
				$"old {_builtIn} new {serialized}");
			Assert.IsTrue(
				string.Equals(json, serialized, StringComparison.OrdinalIgnoreCase),
				$"in {json} out {serialized}");
		} else {
			Assert.AreEqual(builtInSerialized, serialized, "different to old serializer");
			Assert.AreEqual(json, serialized, "did not round trip");
		}
	}

	[Test]
	public void round_trip_objects() {
		RoundTrip(@"{}");
		RoundTrip(@"{""foo"":123,""bar"":456}");
		RoundTrip(@"{""fo o"":[1,2,3]}");
		RoundTrip(@"{"" foo"":{}}");
		RoundTrip(@"{""foo "":true}");
	}

	[Test]
	public void round_trip_arrays() {
		RoundTrip(@"[]");
		RoundTrip(@"[[],3]");
		RoundTrip(@"[3,[]]");
		RoundTrip(@"[{},[[],null],{"""":[4,5,6]}]");
	}

	[Test]
	public void round_trip_values() {
		RoundTrip(@"""stringvalue""");
		RoundTrip(@"34");
		RoundTrip(@"{""foo"":""bar""}");
		RoundTrip(@"[1,4,5,8]");
		RoundTrip(@"true");
		RoundTrip(@"false");
		RoundTrip(@"null");
	}

	[Test]
	public void round_trip_strings() {
		RoundTrip(@"""""");
		RoundTrip(@""" """);
		RoundTrip(@"""foo""");
		RoundTrip(@"""\""""");
		RoundTrip(@"""\\""");
		RoundTrip(@"""/""");
		RoundTrip(@"""\b""");
		RoundTrip(@"""\f""");
		RoundTrip(@"""\n""");
		RoundTrip(@"""\r""");
		RoundTrip(@"""\t""");
		RoundTrip(@"""€""");
	}

	[Test]
	public void round_trip_numbers() {
		//RoundTrip("18446744073709551615");
		//RoundTrip("-18446744073709551615");
		//RoundTrip("18446744073709551616");
		//RoundTrip("-18446744073709551616");
		RoundTrip("5");
		RoundTrip("5E+123", ignoreCase: true);
		RoundTrip("5E-123", ignoreCase: true);
		RoundTrip("-5E-123", ignoreCase: true);
		RoundTrip("-50");
		RoundTrip("0");
		RoundTrip("-0.03");
		RoundTrip("0.0123");
		RoundTrip("-0.0123");
		RoundTrip("-1.23E-123", ignoreCase: true);
	}

	[Test]
	public void big_int() {
		var serialized = _sut.Serialize(new JsBigInt(BigInteger.Parse("20000000000000000000")));
		Assert.AreEqual(@"""20000000000000000000""", serialized);
	}

	[Test]
	public void undefined() {
		var serialized = _sut.Serialize(JsValue.Undefined);
		Assert.AreEqual(@"null", serialized);
	}

	[Test]
	public void undefined_property() {
		var instance = new JsObject(_engine);
		instance.Set("foo", JsValue.Undefined);
		instance.Set("bar", "baz");
		var serialized = _sut.Serialize(instance);
		Assert.AreEqual(@"{""bar"":""baz""}", serialized);
	}

	[Test]
	public void whitespace() {
		// nothing. we only want to test the serializer in this file, not the parser.
	}

	[Test]
	public void big_state() {
		var json = ReadJsonFromFile("big_state.json");
		var instance = _parser.Parse(json);
		var serialized = _sut.Serialize(instance);
		Assert.AreEqual(json, serialized);
	}

	public static string ReadJsonFromFile(string filename)
        {
		var assembly = typeof(when_serializing_state).Assembly;
		var availableFiles = assembly.GetManifestResourceNames();
		var streamName = availableFiles.Where(
			x=> x.StartsWith(typeof(when_serializing_state).Namespace) && x.EndsWith(filename)
			).SingleOrDefault();
		if(streamName == null) throw new InvalidOperationException($"Could not find {filename}");
		using var stream = assembly.GetManifestResourceStream(streamName);

		var doc = JsonDocument.Parse(stream);
		using var ms = new MemoryStream();
		var writer = new Utf8JsonWriter(ms);
		doc.WriteTo(writer);
		writer.Flush();
		ms.Position = 0;
		using var sr = new StreamReader(ms);
		return sr.ReadToEnd();
        }
}
