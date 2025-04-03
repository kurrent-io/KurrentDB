// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Transport.Http;
using HttpStatusCode = System.Net.HttpStatusCode;
using System.Linq;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Transport.Http;

public class ResponseConfiguration(
	int code,
	string description,
	string contentType,
	Encoding encoding,
	IEnumerable<KeyValuePair<string, string>> headers) {
	public readonly int Code = code;
	public readonly string Description = description;
	public readonly string ContentType = contentType;
	public readonly Encoding Encoding = encoding;
	public readonly IEnumerable<KeyValuePair<string, string>> Headers = headers;

	public ResponseConfiguration(int code, string contentType, Encoding encoding, params KeyValuePair<string, string>[] headers)
		: this(code, GetHttpStatusDescription(code), contentType, encoding, headers as IEnumerable<KeyValuePair<string, string>>) {
	}

	public ResponseConfiguration SetCreated(string location) {
		var headers = Headers.ToDictionary(v => v.Key, v => v.Value);
		headers["Location"] = location;
		return new(EventStore.Transport.Http.HttpStatusCode.Created, ContentType, Encoding, headers.ToArray());
	}

	private static string GetHttpStatusDescription(int code) {
		if (code == 200)
			return "OK";
		var status = (HttpStatusCode)code;
		var name = Enum.GetName(typeof(HttpStatusCode), status);
		var result = new StringBuilder(name.Length + 5);
		for (var i = 0; i < name.Length; i++) {
			if (i > 0 && char.IsUpper(name[i]))
				result.Append(' ');
			result.Append(name[i]);
		}

		return result.ToString();
	}

	public ResponseConfiguration(int code, string description, string contentType, Encoding encoding, params KeyValuePair<string, string>[] headers)
		: this(code, description, contentType, encoding, headers as IEnumerable<KeyValuePair<string, string>>) {
	}

	public ResponseConfiguration(int code, string description, string contentType, params KeyValuePair<string, string>[] headers)
		: this(code, description, contentType, Helper.UTF8NoBom, headers as IEnumerable<KeyValuePair<string, string>>) {
	}
}
