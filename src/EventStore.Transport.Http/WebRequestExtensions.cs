// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;

namespace EventStore.Transport.Http;

public static class WebRequestExtensions {
	public static WebResponse EndGetResponseExtended(this WebRequest request, IAsyncResult asyncResult) {
		try {
			return request.EndGetResponse(asyncResult);
		} catch (WebException e) {
			if (e.Response != null)
				return e.Response; //for 404 and 500

			throw;
		}
	}
}
