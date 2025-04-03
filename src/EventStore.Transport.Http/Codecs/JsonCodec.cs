// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Common.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Http.Codecs;

public class JsonCodec : ICodec {
	public static Formatting Formatting = Formatting.Indented;

	private static readonly ILogger Log = Serilog.Log.ForContext<JsonCodec>();

	private static readonly JsonSerializerSettings FromSettings = new JsonSerializerSettings {
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		DefaultValueHandling = DefaultValueHandling.Include,
		MissingMemberHandling = MissingMemberHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
		MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
		Converters = new JsonConverter[] {
			new StringEnumConverter()
		}
	};

	public static readonly JsonSerializerSettings ToSettings = new JsonSerializerSettings {
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateFormatHandling = DateFormatHandling.IsoDateFormat,
		NullValueHandling = NullValueHandling.Include,
		DefaultValueHandling = DefaultValueHandling.Include,
		MissingMemberHandling = MissingMemberHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
		Converters = new JsonConverter[] {new StringEnumConverter()}
	};


	public string ContentType {
		get { return Http.ContentType.Json; }
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
		       || (string.Equals(component.Type, "application", StringComparison.OrdinalIgnoreCase)
		           && (component.Subtype == "*"
		               || string.Equals(component.Subtype, "json", StringComparison.OrdinalIgnoreCase)));
	}

	public T From<T>(string text) {
		try {
			return JsonConvert.DeserializeObject<T>(text, FromSettings);
		} catch (Exception e) {
			Log.Error(e, "'{text}' is not a valid serialized {type}", text, typeof(T).FullName);
			return default(T);
		}
	}

	public string To<T>(T value) {
		if (value == null)
			return "";

		if ((object)value == Empty.Result)
			return Empty.Json;

		try {
			return JsonConvert.SerializeObject(value, Formatting, ToSettings);
		} catch (Exception ex) {
			Log.Error(ex, "Error serializing object {value}", value);
			return null;
		}
	}
}
