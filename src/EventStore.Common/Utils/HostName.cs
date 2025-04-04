// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics;

namespace EventStore.Common.Utils;

public class HostName {
	public static string Combine(Uri responseUrl, string relativeUri, params object[] arg) {
		try {
			return CombineHostNameAndPath(responseUrl, relativeUri, arg);
		} catch (Exception e) {
			Debug.WriteLine("Failed to combine hostname with relative path: {0}", e.Message);
			return relativeUri;
		}
	}

	private static string CombineHostNameAndPath(Uri responseUrl,
		string relativeUri,
		object[] arg) {
		//TODO: encode???
		var path = string.Format(relativeUri, arg);
		if (path.Length > 0 && path[0] == '/')
			path = path.Substring(1);
		return new UriBuilder(responseUrl.Scheme, responseUrl.Host, responseUrl.Port, responseUrl.LocalPath + path)
			.Uri.AbsoluteUri;
	}
}
