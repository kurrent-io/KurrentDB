// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Http.Codecs;

public class XmlCodec : ICodec {
	private static readonly ILogger Log = Serilog.Log.ForContext<XmlCodec>();

	public string ContentType {
		get { return EventStore.Transport.Http.ContentType.Xml; }
	}

	public Encoding Encoding {
		get { return Helper.UTF8NoBom; }
	}

	public bool HasEventIds {
		get { return false; }
	}

	public bool HasEventTypes {
		get { return false; }
	}

	public bool CanParse(MediaType format) {
		return format != null && format.Matches(ContentType, Encoding);
	}

	public bool SuitableForResponse(MediaType component) {
		return component.Type == "*"
		       || (string.Equals(component.Type, "text", StringComparison.OrdinalIgnoreCase)
		           && (component.Subtype == "*"
		               || string.Equals(component.Subtype, "xml", StringComparison.OrdinalIgnoreCase)));
	}

	public T From<T>(string text) {
		if (string.IsNullOrEmpty(text))
			return default(T);

		try {
			using (var reader = new StringReader(text)) {
				return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
			}
		} catch (Exception e) {
			Log.Error(e, "'{text}' is not a valid serialized {type}", text, typeof(T).FullName);
			return default(T);
		}
	}

	public string To<T>(T value) {
		if ((object)value == null)
			return null;

		if ((object)value == Empty.Result)
			return Empty.Xml;

		try {
			using (var memory = new MemoryStream())
			using (var writer = new XmlTextWriter(memory, Helper.UTF8NoBom)) {
				var serializable = value as IXmlSerializable;
				if (serializable != null) {
					writer.WriteStartDocument();
					serializable.WriteXml(writer);
					writer.WriteEndDocument();
				} else {
					new XmlSerializer(typeof(T)).Serialize(writer, value);
				}

				writer.Flush();
				return Helper.UTF8NoBom.GetString(memory.GetBuffer(), 0, (int)memory.Length);
			}
		} catch (Exception exc) {
			Log.Error(exc, "Error serializing object of type {type}", value.GetType().FullName);
			return null;
		}
	}
}
