// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Net.Http.Headers;

namespace EventStore.Core.Tests.Http;

internal static class HttpResponseHeaderExtensions {
	public static string GetLocationAsString(this HttpResponseHeaders headers)
		=> headers.TryGetValues("location", out var values)
			? values.FirstOrDefault()
			: default;

}
