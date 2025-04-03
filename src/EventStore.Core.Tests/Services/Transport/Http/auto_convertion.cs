// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Http.Codecs;
using NUnit.Framework;
using Newtonsoft.Json;
using ContentType = EventStore.Transport.Http.ContentType;

namespace EventStore.Core.Tests.Services.Transport.Http;

internal static class ByteArrayExtensions {
	public static string AsString(this byte[] data) {
		return Helper.UTF8NoBom.GetString(data ?? new byte[0]);
	}
}

internal static class FakeRequest {
	internal const string JsonData = "{\"field1\":\"value1\",\"field2\":\"value2\"}";
	internal const string JsonMetadata = "{\"meta-field1\":\"meta-value1\"}";
	internal const string JsonData2 = "{\"field1\":\"value1\",\"field2\":\"value2\"}";
	internal const string JsonMetadata2 = "{\"meta-field1\":\"meta-value1\"}";

	internal const string XmlData = "<field1>value1</field1><field2>value2</field2>";
	internal const string XmlMetadata = "<meta-field1>meta-value1</meta-field1>";
	internal const string XmlData2 = "<field1>value1</field1><field2>value2</field2>";
	internal const string XmlMetadata2 = "<meta-field1>meta-value1</meta-field1>";

	private static string JsonEventWriteFormat {
		get { return "{{\"eventId\":{0},\"eventType\":{1},\"data\":{2},\"metadata\":{3}}}"; }
	}

	private static string JsonEventReadFormat {
		get {
			return
				"{{\"eventStreamId\":{0},\"eventNumber\":{1},\"eventType\":{2},\"eventId\":{3},\"data\":{4},\"metadata\":{5}}}";
		}
	}

	private static string XmlEventWriteFormat {
		get {
			return
				"<event><eventId>{0}</eventId><eventType>{1}</eventType><data>{2}</data><metadata>{3}</metadata></event>";
		}
	}

	private static string XmlEventReadFormat {
		get {
			return
				"<event><eventStreamId>{0}</eventStreamId><eventNumber>{1}</eventNumber><eventType>{2}</eventType><eventId>{3}</eventId><data>{4}</data><metadata>{5}</metadata></event>";
		}
	}

	public static string GetJsonWrite(string data, string metadata) {
		return GetJsonWrite(new[] {Tuple.Create(data, metadata)});
	}

	public static string GetJsonWrite(params Tuple<string, string>[] events) {
		return string.Format("[{0}]",
			string.Join(",", events.Select(x =>
				string.Format(JsonEventWriteFormat, string.Format("\"{0}\"", Guid.NewGuid()), "\"type\"", x.Item1,
					x.Item2))));
	}

	public static string
		GetJsonEventReadResult(ResolvedEvent evnt, bool dataJson = true, bool metadataJson = true) {
		return string.Format(JsonEventReadFormat,
			WrapIntoQuotes(evnt.Event.EventStreamId),
			evnt.Event.EventNumber,
			WrapIntoQuotes(evnt.Event.EventType),
			WrapIntoQuotes(evnt.Event.EventId.ToString()),
			dataJson ? JsonData : WrapIntoQuotes(AsString(evnt.Event.Data)),
			metadataJson ? JsonMetadata : WrapIntoQuotes(AsString(evnt.Event.Metadata)));
	}

	public static string GetJsonEventsReadResult(IEnumerable<ResolvedEvent> events, bool dataJson = true,
		bool metadataJson = true) {
		return string.Format("[{0}]",
			string.Join(",", events.Select(x => GetJsonEventReadResult(x, dataJson, metadataJson))));
	}

	public static string GetXmlWrite(string data, string metadata) {
		return GetXmlWrite(new[] {Tuple.Create(data, metadata)});
	}

	public static string GetXmlWrite(params Tuple<string, string>[] events) {
		return string.Format("<events>{0}</events>",
			string.Join("\n", events.Select(x =>
				string.Format(XmlEventWriteFormat, Guid.NewGuid(), "type", x.Item1, x.Item2))));
	}

	public static string GetXmlEventReadResult(ResolvedEvent evnt, bool dataJson = true, bool metadataJson = true) {
		return string.Format(XmlEventReadFormat,
			evnt.Event.EventStreamId,
			evnt.Event.EventNumber,
			evnt.Event.EventType,
			evnt.Event.EventId,
			dataJson ? XmlData : AsString(evnt.Event.Data),
			metadataJson ? XmlMetadata : AsString(evnt.Event.Metadata));
	}

	public static string GetXmlEventsReadResult(IEnumerable<ResolvedEvent> events, bool dataJson = true,
		bool metadataJson = true) {
		return string.Format("<events>{0}</events>",
			string.Join("\n", events.Select(x => GetXmlEventReadResult(x, dataJson, metadataJson))));
	}

	public static string AsString(byte[] bytes) {
		return Helper.UTF8NoBom.GetString(bytes ?? new byte[0]);
	}

	public static string AsString(ReadOnlyMemory<byte> bytes) {
		return Helper.UTF8NoBom.GetString(bytes.Span);
	}

	private static string WrapIntoQuotes(string s) {
		return string.Format("\"{0}\"", s);
	}
}

internal abstract class do_not_use_indentation_for_json {
	[OneTimeSetUp]
	public void SetUp() {
		JsonCodec.Formatting = Formatting.None;
	}

	[OneTimeTearDown]
	public void TearDown() {
		JsonCodec.Formatting = Formatting.Indented;
	}

	internal CustomCodec GetCustomCodecForContentType(string contentType) =>
		contentType switch {
			ContentType.EventsJson => Codec.EventsJson,
			ContentType.LegacyEventsJson => Codec.LegacyEventsJson,
			ContentType.EventJson => Codec.EventJson,
			ContentType.LegacyEventJson => Codec.EventJson,
			_ => throw new ArgumentException()
		};

}

[TestFixture]
internal class when_writing_events_and_content_type_is_json : do_not_use_indentation_for_json {
	[Test]
	public void should_just_count_as_body_if_just_json() {
		var codec = Codec.Json;
		var request = FakeRequest.JsonData;
		var id = Guid.NewGuid();
		var type = "EventType";
		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, id, type);
		Assert.NotNull(events);
		Assert.That(events.Length, Is.EqualTo(1));

		Assert.IsTrue(events[0].IsJson);
		Assert.AreEqual(events[0].EventId, id);
		Assert.AreEqual(events[0].EventType, type);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(string.Empty));
	}
}

[TestFixture]
internal class when_writing_events_and_content_type_is_xml {
	[Test]
	public void should_just_count_as_body_if_just_xml() {
		var codec = Codec.Xml;
		var request = FakeRequest.XmlData;
		var id = Guid.NewGuid();
		var type = "EventType";
		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, id, type);
		Assert.NotNull(events);
		Assert.That(events.Length, Is.EqualTo(1));

		Assert.IsFalse(events[0].IsJson);
		Assert.AreEqual(events[0].EventId, id);
		Assert.AreEqual(events[0].EventType, type);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.XmlData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(string.Empty));
	}
}

[TestFixture(ContentType.EventsJson)]
[TestFixture(ContentType.LegacyEventsJson)]
internal class when_writing_events_and_content_type_is_events_json(string contentType) : do_not_use_indentation_for_json {
	[Test]
	public void should_store_data_as_json_if_valid_and_metadata_as_string_if_not() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(new[] {
			Tuple.Create(FakeRequest.JsonData, "\"metadata\""),
			Tuple.Create(FakeRequest.JsonData2, "\"metadata2\"")
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsTrue(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo("metadata"));

		Assert.IsTrue(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo(FakeRequest.JsonData2));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo("metadata2"));
	}

	[Test]
	public void should_store_metadata_as_json_if_its_valid_and_data_as_string_if_its_not() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(new[] {
			Tuple.Create("\"data\"", FakeRequest.JsonMetadata),
			Tuple.Create("\"data2\"", FakeRequest.JsonMetadata2)
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsTrue(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo("data"));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));

		Assert.IsTrue(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo("data2"));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata2));
	}

	[Test]
	public void should_store_both_data_and_metadata_as_json_if_both_are_valid_json_objects() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(new[] {
			Tuple.Create(FakeRequest.JsonData, FakeRequest.JsonMetadata),
			Tuple.Create(FakeRequest.JsonData2, FakeRequest.JsonMetadata2)
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsTrue(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));

		Assert.IsTrue(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo(FakeRequest.JsonData2));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata2));
	}

	[Test]
	public void should_store_both_data_and_metadata_as_string_if_both_are_not_valid_json_objects() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(new[] {
			Tuple.Create("\"data\"", "\"metadata\""),
			Tuple.Create("\"data2\"", "\"metadata2\"")
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsFalse(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo("data"));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo("metadata"));

		Assert.IsFalse(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo("data2"));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo("metadata2"));
	}

	[Test]
	public void should_do_its_best_at_preserving_data_format_with_multiple_events() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(new[] {
			Tuple.Create(FakeRequest.JsonData, FakeRequest.JsonMetadata),
			Tuple.Create("\"data2\"", "\"metadata2\"")
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsTrue(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));

		Assert.IsFalse(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo("data2"));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo("metadata2"));
	}
}

[TestFixture]
internal class when_writing_events_and_content_type_is_events_xml : do_not_use_indentation_for_json {
	[Test]
	public void should_convert_data_to_json_if_its_valid_xobject_and_metadata_as_string_if_its_not() {
		var codec = Codec.EventsXml;
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var converted = events.Single();

		Assert.That(converted.IsJson);
		Assert.That(converted.Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(converted.Metadata.AsString(), Is.EqualTo("metadata"));
	}

	[Test]
	public void should_convert_metadata_to_json_if_its_valid_xobject_and_data_as_string_if_its_not() {
		var codec = Codec.EventsXml;
		var request = FakeRequest.GetXmlWrite("data", FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var converted = events.Single();

		Assert.That(converted.IsJson);
		Assert.That(converted.Data.AsString(), Is.EqualTo("data"));
		Assert.That(converted.Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));
	}

	[Test]
	public void should_convert_data_and_metadata_to_json_if_both_are_valid_xobjects() {
		var codec = Codec.EventsXml;
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var converted = events.Single();

		Assert.That(converted.IsJson);
		Assert.That(converted.Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(converted.Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));
	}

	[Test]
	public void should_store_both_data_and_metadata_as_string_if_both_are_not_valid_xobjects_objects() {
		var codec = Codec.EventsXml;
		var request = FakeRequest.GetXmlWrite("data", "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var converted = events.Single();

		Assert.That(!converted.IsJson);
		Assert.That(converted.Data.AsString(), Is.EqualTo("data"));
		Assert.That(converted.Metadata.AsString(), Is.EqualTo("metadata"));
	}

	[Test]
	public void should_do_its_best_at_preserving_data_format_with_multiple_events() {
		var codec = Codec.EventsXml;
		var request = FakeRequest.GetXmlWrite(new[] {
			Tuple.Create(FakeRequest.XmlData, FakeRequest.XmlMetadata),
			Tuple.Create("data2", "metadata2")
		});

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		Assert.That(events.Length, Is.EqualTo(2));

		Assert.IsTrue(events[0].IsJson);
		Assert.That(events[0].Data.AsString(), Is.EqualTo(FakeRequest.JsonData));
		Assert.That(events[0].Metadata.AsString(), Is.EqualTo(FakeRequest.JsonMetadata));

		Assert.IsFalse(events[1].IsJson);
		Assert.That(events[1].Data.AsString(), Is.EqualTo("data2"));
		Assert.That(events[1].Metadata.AsString(), Is.EqualTo("metadata2"));
	}
}

[TestFixture(ContentType.EventJson)]
[TestFixture(ContentType.LegacyEventJson)]
internal class when_reading_events_and_accept_type_is_json(string contentType) : do_not_use_indentation_for_json {
	[Test]
	public void should_return_json_data_if_data_was_originally_written_as_xobject_and_metadata_as_string() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: true, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, codec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_json_data_if_data_was_originally_written_as_jobject_and_metadata_as_string(string writtenContentType) {
		var writtenCodec = GetCustomCodecForContentType(writtenContentType);
		var requestCodec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(FakeRequest.JsonData, "\"metadata\"");

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), writtenCodec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: true, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, requestCodec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_json_metadata_if_metadata_was_originally_written_as_xobject_and_data_as_string() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetXmlWrite("data", FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();

		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);

		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: false, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, codec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_json_metadata_if_metadata_was_originally_written_as_jobject_and_data_as_string(string writtenContentType) {
		var writtenCodec = GetCustomCodecForContentType(writtenContentType);
		var requestCodec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite("\"data\"", FakeRequest.JsonMetadata);

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), writtenCodec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: false, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, requestCodec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_json_data_and_json_metadata_if_both_were_written_as_xobjects() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: true, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, codec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_json_data_and_json_metadata_if_both_were_written_as_jobjects(string writtenContentType) {
		var writtenCodec = GetCustomCodecForContentType(writtenContentType);
		var requestCodec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(FakeRequest.JsonData, FakeRequest.JsonMetadata);

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), writtenCodec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: true, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, requestCodec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_string_data_and_string_metadata_if_both_were_written_as_string_using_json_write(string writtenContentType) {
		var writtenCodec = GetCustomCodecForContentType(writtenContentType);
		var requestCodec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite("\"data\"", "\"metadata\"");

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), writtenCodec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: false, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, requestCodec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_string_data_and_string_metadata_if_both_were_written_as_string_using_xml_write() {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetXmlWrite("data", "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetJsonEventReadResult(resolvedEvent, dataJson: false, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, codec);

		Assert.That(converted, Is.EqualTo(expected));
	}

	private ResolvedEvent GenerateResolvedEvent(byte[] data, byte[] metadata) {
		return ResolvedEvent.ForUnresolvedEvent(new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			"stream", 0,
			DateTime.MinValue, PrepareFlags.IsJson, "type", data, metadata));
	}
}

[TestFixture]
internal class when_reading_events_and_accept_type_is_xml : do_not_use_indentation_for_json {
	[Test]
	public void should_return_xml_data_if_data_was_originally_written_as_xobject_and_metadata_as_string() {
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: true, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_xml_data_if_data_was_originally_written_as_jobject_and_metadata_as_string(string contentType) {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(FakeRequest.JsonData, "\"metadata\"");

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: true, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_xml_metadata_if_metadata_was_originally_written_as_xobject_and_data_as_string() {
		var request = FakeRequest.GetXmlWrite("data", FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: false, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_xml_metadata_if_metadata_was_originally_written_as_jobject_and_data_as_string(string contentType) {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite("\"data\"", FakeRequest.JsonMetadata);

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: false, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_xml_data_and_xml_metadata_if_both_were_written_as_xobjects() {
		var request = FakeRequest.GetXmlWrite(FakeRequest.XmlData, FakeRequest.XmlMetadata);

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: true, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_xml_data_and_xml_metadata_if_both_were_written_as_jobjects(string contentType) {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite(FakeRequest.JsonData, FakeRequest.JsonMetadata);

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: true, metadataJson: true);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	[TestCase(ContentType.EventsJson)]
	[TestCase(ContentType.LegacyEventsJson)]
	public void should_return_string_data_and_string_metadata_if_both_were_written_as_string_using_json_events_write(string contentType) {
		var codec = GetCustomCodecForContentType(contentType);
		var request = FakeRequest.GetJsonWrite("\"data\"", "\"metadata\"");

		var events =
			AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), codec, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: false, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	[Test]
	public void should_return_string_data_and_string_metadata_if_both_were_written_as_string_using_xml_events_write() {
		var request = FakeRequest.GetXmlWrite("data", "metadata");

		var events = AutoEventConverter.SmartParse(Helper.UTF8NoBom.GetBytes(request), Codec.EventsXml, Guid.Empty);
		var evnt = events.Single();
		var resolvedEvent = GenerateResolvedEvent(evnt.Data, evnt.Metadata);
		var expected = FakeRequest.GetXmlEventReadResult(resolvedEvent, dataJson: false, metadataJson: false);
		var converted = AutoEventConverter.SmartFormat(resolvedEvent, Codec.EventXml);

		Assert.That(converted, Is.EqualTo(expected));
	}

	private ResolvedEvent GenerateResolvedEvent(byte[] data, byte[] metadata) {
		return ResolvedEvent.ForUnresolvedEvent(new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			"stream", 0,
			DateTime.MinValue, PrepareFlags.IsJson, "type", data, metadata));
	}
}
