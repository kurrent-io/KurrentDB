// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Formatting = Newtonsoft.Json.Formatting;

namespace EventStore.Common.Utils;

public static class Json {
	public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateFormatHandling = DateFormatHandling.IsoDateFormat,
		NullValueHandling = NullValueHandling.Ignore,
		DefaultValueHandling = DefaultValueHandling.Ignore,
		MissingMemberHandling = MissingMemberHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
		Converters = new JsonConverter[] {new StringEnumConverter()}
	};

	public static byte[] ToJsonBytes(this object source) {
		string instring = JsonConvert.SerializeObject(source, Formatting.Indented, JsonSettings);
		return Helper.UTF8NoBom.GetBytes(instring);
	}

	public static string ToJson(this object source) {
		string instring = JsonConvert.SerializeObject(source, Formatting.Indented, JsonSettings);
		return instring;
	}

	public static string ToCanonicalJson(this object source) {
		string instring = JsonConvert.SerializeObject(source);
		return instring;
	}

	public static T ParseJson<T>(this string json) {
		var result = JsonConvert.DeserializeObject<T>(json, JsonSettings);
		return result;
	}

	public static T ParseJson<T>(this byte[] json) {
		var result = JsonConvert.DeserializeObject<T>(Helper.UTF8NoBom.GetString(json), JsonSettings);
		return result;
	}

	public static T ParseJson<T>(this ReadOnlyMemory<byte> json) {
		var result = JsonConvert.DeserializeObject<T>(Helper.UTF8NoBom.GetString(json.Span), JsonSettings);
		return result;
	}

	private static JsonReaderOptions JsonReaderOptions = new() {
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
		MaxDepth = 64, // default - just being explicit
	};

	public static bool IsValidUtf8Json(this ReadOnlyMemory<byte> value) {
		// Don't bother letting an Exception getting thrown.
		if (value.IsEmpty)
			return false;

		try {
			var reader = new Utf8JsonReader(value.Span, JsonReaderOptions);
			while (reader.Read())
				reader.Skip();
			return true;
		} catch {
			return false;
		}
	}
}
