// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Services.Transport.Http;

public class ResponseConfiguration {
	public readonly int Code;
	public readonly string Description;
	public readonly string ContentType;
	public readonly Encoding Encoding;
	public readonly IEnumerable<KeyValuePair<string, string>> Headers;

	public ResponseConfiguration(int code, string contentType, Encoding encoding,
		params KeyValuePair<string, string>[] headers)
		: this(code, GetHttpStatusDescription(code), contentType, encoding,
			headers as IEnumerable<KeyValuePair<string, string>>) {
	}

	public ResponseConfiguration SetCreated(string location) {
		var headers = Headers.ToDictionary(v => v.Key, v => v.Value);
		headers["Location"] = location;
		return new ResponseConfiguration(EventStore.Transport.Http.HttpStatusCode.Created, ContentType, Encoding,
			headers.ToArray());
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

	public ResponseConfiguration(int code, string description, string contentType, Encoding encoding,
		params KeyValuePair<string, string>[] headers)
		: this(code, description, contentType, encoding, headers as IEnumerable<KeyValuePair<string, string>>) {
	}

	public ResponseConfiguration(int code, string description, string contentType, Encoding encoding,
		IEnumerable<KeyValuePair<string, string>> headers) {
		Code = code;
		Description = description;
		ContentType = contentType;
		Encoding = encoding;
		Headers = headers;
	}
}
