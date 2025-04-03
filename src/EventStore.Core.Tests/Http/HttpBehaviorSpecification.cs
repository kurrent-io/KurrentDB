// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using Newtonsoft.Json;
using EventStore.ClientAPI.Transport.Http;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;
using ContentTypeConstant = EventStore.Transport.Http.ContentType;

namespace EventStore.Core.Tests.Http;

public abstract class HttpBehaviorSpecification : HttpBehaviorSpecification<LogFormat.V2, string>;

public abstract class HttpBehaviorSpecification<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected MiniNode<TLogFormat, TStreamId> _node;
	protected IEventStoreConnection _connection;
	protected HttpResponseMessage _lastResponse;
	protected string _lastResponseBody;
	protected byte[] _lastResponseBytes;
	protected JsonException _lastJsonException;

	private readonly System.Collections.Generic.List<HttpResponseMessage> _allResponses =
		new System.Collections.Generic.List<HttpResponseMessage>();

	private string _tag = default;
	private NetworkCredential _defaultCredentials;
	protected HttpClient _client => _node.HttpClient;

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_node = CreateMiniNode();
		await _node.Start();

		_connection = TestConnection.Create(_node.TcpEndPoint);
		await _connection.ConnectAsync();

		_lastResponse = null;
		_lastResponseBody = null;
		_lastResponseBytes = null;
		_lastJsonException = null;

		await Given().WithTimeout(TimeSpan.FromMinutes(5));

		await When().WithTimeout(TimeSpan.FromMinutes(5));
	}

	public string TestStream {
		get { return "/streams/test" + Tag; }
	}

	public string TestStreamName {
		get { return "test" + Tag; }
	}

	public string TestMetadataStream {
		get { return "/streams/$$test" + Tag; }
	}

	public string Tag {
		get { return _tag; }
	}

	protected virtual MiniNode<TLogFormat, TStreamId> CreateMiniNode() =>
		new(PathName);

	public override async Task TestFixtureTearDown() {
		_connection.Close();
		await _node.Shutdown();
		await base.TestFixtureTearDown();
		foreach (var response in _allResponses) {
			response?.Dispose();
		}
	}

	protected HttpRequestMessage CreateRequest(
		string path, string extra, string method, string contentType, NetworkCredential credentials = null,
		NameValueCollection headers = null) {
		credentials ??= _defaultCredentials;
		var uri = MakeUrl(path, extra);
		var httpWebRequest = new HttpRequestMessage(new System.Net.Http.HttpMethod(method), uri);
		if (headers != null) {
			foreach (var key in headers.AllKeys) {
				httpWebRequest.Headers.Add(key, headers.GetValues(key));
			}
		}
		if (credentials != null) {
			httpWebRequest.Headers.Add("authorization", $"Basic {GetAuthorizationHeader(credentials)}");
		}

		return httpWebRequest;
	}

	protected HttpRequestMessage CreateRequest(string path, string method, NetworkCredential credentials = null,
		string extra = null)
		=> CreateRequest(path, extra, method, null, credentials);

	protected static string GetAuthorizationHeader(NetworkCredential credentials)
		=> Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.UserName}:{credentials.Password}"));

	protected Uri MakeUrl(string path, string extra = "") {
		var supplied = string.IsNullOrWhiteSpace(extra)
			? new Uri(path, UriKind.RelativeOrAbsolute)
			: new Uri(path + "?" + extra, UriKind.RelativeOrAbsolute);

		if (supplied.IsAbsoluteUri && !supplied.IsFile)
			return supplied;

		var httpEndPoint = _node.HttpEndPoint;
		var x = new UriBuilder("http", httpEndPoint.Address.ToString(), httpEndPoint.Port, path);
		x.Query = extra;
		Console.WriteLine(new string('*', 50) + Environment.NewLine + path + Environment.NewLine + extra +
						  Environment.NewLine + x.Uri + Environment.NewLine + new string('*', 50));

		return x.Uri;
	}

	protected Task<HttpResponseMessage> MakeJsonPut<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRawJsonPostRequest(path, "PUT", body, credentials, extra);
		return GetRequestResponse(request);
	}


	protected Task<HttpResponseMessage> MakeJsonPost<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRawJsonPostRequest(path, "POST", body, credentials, extra);
		return GetRequestResponse(request);
	}

	protected Task<HttpResponseMessage> MakeArrayEventsPost<T>(string path, T body, NetworkCredential credentials = null, string extra = null, string contentType = null) {
		credentials ??= _defaultCredentials;
		var request = CreateEventsJsonPostRequest(path, "POST", body, credentials, extra, contentType);
		return GetRequestResponse(request);
	}

	protected Task<HttpResponseMessage> MakeRawJsonPost<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRawJsonPostRequest(path, "POST", body, credentials, extra);
		return GetRequestResponse(request);
	}

	protected Task<HttpResponseMessage> MakeRawJsonPut<T>(string path, T body, NetworkCredential credentials, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRawJsonPostRequest(path, "PUT", body, credentials, extra);
		return GetRequestResponse(request);
	}

	protected Task<HttpResponseMessage> MakeDelete(string path, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRequest(path, "DELETE", credentials, extra);
		return GetRequestResponse(request);
	}

	protected Task<HttpResponseMessage> MakePost(string path, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateJsonPostRequest(path, credentials, extra);
		return GetRequestResponse(request);
	}

	protected async Task<XDocument> GetAtomXml(Uri uri, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		await Get(uri.ToString(), extra, ContentType.Atom, credentials);
		return XDocument.Parse(_lastResponseBody);
	}

	protected async Task<XDocument> GetXml(Uri uri, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		await Get(uri.ToString(), null, ContentType.Xml, credentials);
		return XDocument.Parse(_lastResponseBody);
	}

	protected async Task<T> GetJson<T>(string path, string accept = null, NetworkCredential credentials = null,
		NameValueCollection headers = null, string extra = null) {
		credentials ??= _defaultCredentials;
		await Get(path, extra, accept, credentials, headers: headers);
		try {
			return _lastResponseBody.ParseJson<T>();
		} catch (JsonException ex) {
			_lastJsonException = ex;
			return default;
		}
	}

	protected async Task<T> GetJsonWithoutAcceptHeader<T>(string path) {
		var request = CreateRequest(path, "", "GET", null);
		_lastResponse = await GetRequestResponse(request);
		using var memoryStream = new MemoryStream();
		await _lastResponse.Content.CopyToAsync(memoryStream);
		var bytes = memoryStream.ToArray();
		_lastResponseBody = Helper.UTF8NoBom.GetString(bytes);
		try {
			return _lastResponseBody.ParseJson<T>();
		} catch (JsonException ex) {
			_lastJsonException = ex;
			return default;
		}
	}

	protected async Task Get(string path, string extra, string accept = null, NetworkCredential credentials = null,
		bool setAcceptHeader = true, NameValueCollection headers = null) {
		credentials = credentials ?? _defaultCredentials;
		var request = CreateRequest(path, extra, "GET", null, credentials, headers);
		if (setAcceptHeader) {
			request.Headers.Add("accept", accept ?? ContentTypeConstant.Json);
		}

		_lastResponse = await GetRequestResponse(request);
		using var memoryStream = new MemoryStream();
		await _lastResponse.Content.CopyToAsync(memoryStream);
		var bytes = memoryStream.ToArray();
		_lastResponseBytes = bytes;
		_lastResponseBody = Helper.UTF8NoBom.GetString(bytes);
	}

	protected async Task<HttpResponseMessage> GetRequestResponse(HttpRequestMessage request) {
		var response = await _client.SendAsync(request);
		_allResponses.Add(response);

		return response;
	}

	protected readonly JsonSerializerSettings TestJsonSettings = new JsonSerializerSettings {
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateFormatHandling = DateFormatHandling.IsoDateFormat,
		NullValueHandling = NullValueHandling.Ignore,
		DefaultValueHandling = DefaultValueHandling.Include,
		MissingMemberHandling = MissingMemberHandling.Ignore,
		TypeNameHandling = TypeNameHandling.None,
		Converters = new JsonConverter[] { new StringEnumConverter() }
	};

	protected byte[] ToJsonBytes(object source) {
		string instring = JsonConvert.SerializeObject(source, Newtonsoft.Json.Formatting.Indented, TestJsonSettings);
		return Helper.UTF8NoBom.GetBytes(instring);
	}

	protected HttpRequestMessage CreateEventsJsonPostRequest<T>(
		string path, string method, T body, NetworkCredential credentials = null, string extra = null, string contentType = null) {
		credentials = credentials ?? _defaultCredentials;
		var request = CreateRequest(path, extra, method, contentType ?? ContentTypeConstant.EventsJson, credentials);
		request.Content = new ByteArrayContent(ToJsonBytes(body)) {
			Headers = { ContentType = new MediaTypeHeaderValue(contentType ?? ContentTypeConstant.EventsJson) }
		};
		return request;
	}

	protected HttpRequestMessage CreateRawJsonPostRequest<T>(
		string path, string method, T body, NetworkCredential credentials = null, string extra = null) {
		credentials ??= _defaultCredentials;
		var request = CreateRequest(path, extra, method, ContentTypeConstant.Json, credentials);
		request.Content = new ByteArrayContent(ToJsonBytes(body)) {
			Headers = { ContentType = new MediaTypeHeaderValue(ContentTypeConstant.Json) }
		};
		return request;
	}
	private HttpRequestMessage CreateJsonPostRequest(string path, NetworkCredential credentials = null, string extra = null) {
		credentials = credentials ?? _defaultCredentials;
		var request = CreateRequest(path, "POST", credentials, extra);

		return request;
	}
	protected void SetDefaultCredentials(NetworkCredential credentials) {
		_defaultCredentials = credentials;
	}

	protected abstract Task Given();
	protected abstract Task When();
}
