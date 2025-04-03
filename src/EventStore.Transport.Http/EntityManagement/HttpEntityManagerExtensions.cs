// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http.EntityManagement;

public static class HttpEntityManagerExtensions {
	public static void ReplyStatus(this HttpEntityManager self, int code, string description,
		Action<Exception> onError, IEnumerable<KeyValuePair<string, string>> headers = null) {
		self.Reply(null, code, description, null, null, null, onError);
	}

	public static void ReplyContent(this HttpEntityManager self,
		byte[] response,
		int code,
		string description,
		string type,
		IEnumerable<KeyValuePair<string, string>> headers,
		Action<Exception> onError) {
		self.Reply(response, code, description, type, null, headers, onError);
	}

	public static void ReplyTextContent(this HttpEntityManager self,
		string response,
		int code,
		string description,
		string type,
		IEnumerable<KeyValuePair<string, string>> headers,
		Action<Exception> onError) {
		//TODO: add encoding header???
		self.Reply(Helper.UTF8NoBom.GetBytes(response ?? string.Empty), code, description, type, Helper.UTF8NoBom,
			headers, onError);
	}

	public static void ContinueReplyTextContent(
		this HttpEntityManager self, string response, Action<Exception> onError, Action completed) {
		//TODO: add encoding header???
		var bytes = Helper.UTF8NoBom.GetBytes(response ?? string.Empty);
		self.ContinueReply(bytes, onError, completed);
	}

	public static void ReadTextRequestAsync(
		this HttpEntityManager self, Action<HttpEntityManager, string> onSuccess, Action<Exception> onError) {
		self.ReadRequestAsync(
			(manager, bytes) => {
				int offset = 0;

				// check for UTF-8 BOM (0xEF, 0xBB, 0xBF) and skip it safely, if any
				if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
					offset = 3;

				onSuccess(manager, Helper.UTF8NoBom.GetString(bytes, offset, bytes.Length - offset));
			},
			onError);
	}
}
