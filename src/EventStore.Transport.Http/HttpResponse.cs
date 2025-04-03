// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;

namespace EventStore.Transport.Http;

public class HttpResponse {
	public string CharacterSet { get; private set; }

	public string ContentEncoding { get; private set; }
	public long ContentLength { get; private set; }
	public string ContentType { get; private set; }

	//public CookieCollection Cookies { get; private set; }
	public HttpResponseHeaders Headers { get; private set; }

	//public bool IsFromCache { get; private set; }
	//public bool IsMutuallyAuthenticated { get; private set; }//TODO TR: not implemented in mono
	//public DateTime LastModified { get; private set; }

	public string Method { get; private set; }
	//public Version ProtocolVersion { get; private set; }

	//public Uri ResponseUri { get; private set; }
	//public string Server { get; private set; }

	public int HttpStatusCode { get; private set; }
	public string StatusDescription { get; private set; }

	public string Body { get; internal set; }

	public HttpResponse(HttpResponseMessage responseMessage) {
		ContentEncoding = responseMessage.Content.Headers.ContentEncoding.FirstOrDefault();
		ContentLength = responseMessage.Content.Headers.ContentLength.Value;

		if (responseMessage.Content.Headers.ContentType != null) {
			CharacterSet = responseMessage.Content.Headers.ContentType.CharSet;
			ContentType = responseMessage.Content.Headers.ContentType.MediaType;
		}

		//Cookies = httpWebResponse.Cookies;
		Headers = responseMessage.Headers;

		//IsFromCache = httpWebResponse.IsFromCache;
		//IsMutuallyAuthenticated = httpWebResponse.IsMutuallyAuthenticated;

		//LastModified = httpWebResponse.LastModified;

		Method = responseMessage.RequestMessage.Method.ToString();
		//ProtocolVersion = httpWebResponse.ProtocolVersion;

		//ResponseUri = httpWebResponse.ResponseUri;
		//Server = httpWebResponse.Server;

		HttpStatusCode = (int)responseMessage.StatusCode;
		StatusDescription = responseMessage.ReasonPhrase;
	}
}
