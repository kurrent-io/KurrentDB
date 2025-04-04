// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using System.Xml.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http;

public static class AutoEventConverter {
	private static readonly ILogger Log =
		Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "AutoEventConverter");

	public static object SmartFormat(ResolvedEvent evnt, ICodec targetCodec) {
		var dto = CreateDataDto(evnt);

		switch (targetCodec.ContentType) {
			case ContentType.Raw:
				return evnt.Event.Data.ToArray();
			case ContentType.Xml:
			case ContentType.ApplicationXml: {
				if (evnt.Event.Data.IsEmpty)
					return "<data />";

				var serializeObject = JsonConvert.SerializeObject(dto.data);
				var deserializeXmlNode = JsonConvert.DeserializeXmlNode(serializeObject, "data");
				return deserializeXmlNode.InnerXml;
			}
			case ContentType.Json:
				return targetCodec.To(dto.data);


			case ContentType.Atom:
			case ContentType.EventXml: {
				var serializeObject = JsonConvert.SerializeObject(dto);
				var deserializeXmlNode = JsonConvert.DeserializeXmlNode(serializeObject, "event");
				return deserializeXmlNode.InnerXml;
			}

			case ContentType.EventJson:
				return targetCodec.To(dto);


			default:
				throw new NotSupportedException();
		}
	}

	public static HttpClientMessageDto.ReadEventCompletedText CreateDataDto(ResolvedEvent evnt) {
		var dto = new HttpClientMessageDto.ReadEventCompletedText(evnt);
		if (evnt.Event.Flags.HasFlag(PrepareFlags.IsJson)) {
			var deserializedData = Codec.Json.From<object>((string)dto.data);
			var deserializedMetadata = Codec.Json.From<object>((string)dto.metadata);

			if (deserializedData != null)
				dto.data = deserializedData;
			if (deserializedMetadata != null)
				dto.metadata = deserializedMetadata;
		}

		return dto;
	}

	//TODO GFY THERE IS WAY TOO MUCH COPYING/SERIALIZING/DESERIALIZING HERE!
	public static Event[] SmartParse(byte[] request, ICodec sourceCodec, Guid includedId,
		string includedType = null) {
		switch (sourceCodec.ContentType) {
			case ContentType.Raw:
				return LoadRaw(request, includedId, includedType);
			case ContentType.Json:
				return LoadRaw(sourceCodec.Encoding.GetString(request), true, includedId, includedType);
			case ContentType.EventJson:
			case ContentType.EventsJson:
			case ContentType.AtomJson:
				var writeEvents = LoadFromJson(sourceCodec.Encoding.GetString(request));
				if (writeEvents.IsEmpty())
					return null;
				return Parse(writeEvents);

			case ContentType.ApplicationXml:
			case ContentType.Xml:
				return LoadRaw(sourceCodec.Encoding.GetString(request), false, includedId, includedType);
			case ContentType.EventXml:
			case ContentType.EventsXml:
			case ContentType.Atom:
				var writeEvents2 = LoadFromXml(sourceCodec.Encoding.GetString(request));
				if (writeEvents2.IsEmpty())
					return null;
				return Parse(writeEvents2);
			default:
				return null;
		}
	}

	private static Event[] LoadRaw(byte[] data, Guid includedId, string includedType) {
		var ret = new Event[1];
		ret[0] = new Event(includedId, includedType, false, data, null);
		return ret;
	}

	private static Event[] LoadRaw(string data, bool isJson, Guid includedId, string includedType) {
		var ret = new Event[1];
		ret[0] = new Event(includedId, includedType, isJson, data, null);
		return ret;
	}

	private static HttpClientMessageDto.ClientEventDynamic[] LoadFromJson(string json) {
		return Codec.Json.From<HttpClientMessageDto.ClientEventDynamic[]>(json);
	}

	private static HttpClientMessageDto.ClientEventDynamic[] LoadFromXml(string xml) {
		try {
			XDocument doc = XDocument.Parse(xml);

			XNamespace jsonNsValue = "http://james.newtonking.com/projects/json";
			XName jsonNsName = XNamespace.Xmlns + "json";

			doc.Root.SetAttributeValue(jsonNsName, jsonNsValue);

			var events = doc.Root.Elements() /*.ToArray()*/;
			foreach (var @event in events) {
				@event.Name = "events";
				@event.SetAttributeValue(jsonNsValue + "Array", "true");
			}
			//doc.Root.ReplaceNodes(events);
			//                foreach (var element in doc.Root.Descendants("data").Concat(doc.Root.Descendants("metadata")))
			//                {
			//                    element.RemoveAttributes();
			//                }

			var json = JsonConvert.SerializeXNode(doc.Root, Formatting.None, true);
			var root = JsonConvert.DeserializeObject<HttpClientMessageDto.WriteEventsDynamic>(json);
			return root.events;
			//                var root = JsonConvert.DeserializeObject<JObject>(json);
			//                var dynamicEvents = root.ToObject<HttpClientMessageDto.WriteEventsDynamic>();
			//                return dynamicEvents.events;
		} catch (Exception e) {
			Log.Information(e, "Failed to load xml. Invalid format");
			return null;
		}
	}

	private static Event[] Parse(HttpClientMessageDto.ClientEventDynamic[] dynamicEvents) {
		var events = new Event[dynamicEvents.Length];
		for (int i = 0, n = dynamicEvents.Length; i < n; ++i) {
			var textEvent = dynamicEvents[i];
			bool dataIsJson;
			bool metadataIsJson;
			var data = AsBytes(textEvent.data, out dataIsJson);
			var metadata = AsBytes(textEvent.metadata, out metadataIsJson);

			events[i] = new Event(textEvent.eventId, textEvent.eventType, dataIsJson || metadataIsJson, data,
				metadata);
		}

		return events.ToArray();
	}

	private static byte[] AsBytes(object obj, out bool isJson) {
		if (obj is JObject || obj is JArray) {
			isJson = true;
			return Helper.UTF8NoBom.GetBytes(Codec.Json.To(obj));
		} else if (obj is string) {
			try {
				var jsonObject = JsonConvert.DeserializeObject((string)obj);
				if (jsonObject is JObject || jsonObject is JArray) {
					isJson = true;
					return Helper.UTF8NoBom.GetBytes(Codec.Json.To(jsonObject));
				} else
					throw new JsonException();
			} catch (JsonException) {
				isJson = false;
				return Helper.UTF8NoBom.GetBytes((obj as string));
			}
		}
		isJson = false;
		return Helper.UTF8NoBom.GetBytes(string.Empty);
	}
}
