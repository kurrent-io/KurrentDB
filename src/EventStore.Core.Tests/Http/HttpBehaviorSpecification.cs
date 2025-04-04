using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Transport.Http;
using EventStore.Common.Utils;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace EventStore.Core.Tests.Http {
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

		protected virtual bool GivenSkipInitializeStandardUsersCheck() => false;

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

			if (supplied.IsAbsoluteUri && !supplied.IsFile) // NOTE: is file imporant for mono
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

		protected Task<HttpResponseMessage> MakeArrayEventsPost<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateEventsJsonPostRequest(path, "POST", body, credentials, extra);
			return GetRequestResponse(request);
		}

		protected Task<HttpResponseMessage> MakeRawJsonPost<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateRawJsonPostRequest(path, "POST", body, credentials, extra);
			return GetRequestResponse(request);
		}

		protected async Task<JObject> MakeJsonPostWithJsonResponse<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateRawJsonPostRequest(path, "POST", body, credentials, extra);
			_lastResponse = await GetRequestResponse(request);
			var memoryStream = new MemoryStream();
			await _lastResponse.Content.CopyToAsync(memoryStream);
			var bytes = memoryStream.ToArray();
			_lastResponseBody = Helper.UTF8NoBom.GetString(bytes);
			try {
				return _lastResponseBody.ParseJson<JObject>();
			} catch (JsonException ex) {
				_lastJsonException = ex;
				return default;
			}
		}

		protected async Task<JObject> MakeJsonEventsPostWithJsonResponse<T>(string path, T body, NetworkCredential credentials = null, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateEventsJsonPostRequest(path, "POST", body, credentials, extra);
			_lastResponse = await GetRequestResponse(request);
			var memoryStream = new MemoryStream();
			await _lastResponse.Content.CopyToAsync(memoryStream);
			var bytes = memoryStream.ToArray();
			_lastResponseBody = Helper.UTF8NoBom.GetString(bytes);
			try {
				return _lastResponseBody.ParseJson<JObject>();
			} catch (JsonException ex) {
				_lastJsonException = ex;
				return default;
			}
		}


		protected Task<HttpResponseMessage> MakeEventsJsonPut<T>(string path, T body, NetworkCredential credentials, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateEventsJsonPostRequest(path, "PUT", body, credentials, extra);
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

		protected async Task<T> GetJson2<T>(string path, string extra, string accept = null, NetworkCredential credentials = null) {
			credentials ??= _defaultCredentials;
			await Get(path, extra, accept, credentials);
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
				request.Headers.Add("accept", accept ?? "application/json");
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

			/*if (_dumpRequest != null) {
				var bytes = _dumpRequest(request);
				if (bytes != null)
					Console.WriteLine(Encoding.ASCII.GetString(bytes, 0, GetBytesLength(bytes)).TrimEnd('\0'));
			}

			if (_dumpRequest2 != null) {
				var bytes = _dumpRequest2(request);
				if (bytes != null)
					Console.WriteLine(Encoding.ASCII.GetString(bytes, 0, GetBytesLength(bytes)).TrimEnd('\0'));
			}

			Console.WriteLine();
			if (_dumpResponse != null) {
				var bytes = _dumpResponse(response);
				var len = _dumpResponse2(response);
				if (bytes != null)
					Console.WriteLine(Encoding.ASCII.GetString(bytes, 0, len).TrimEnd('\0'));
			}*/

			return response;
		}

		private int GetBytesLength(byte[] bytes) {
			var index = Array.IndexOf(bytes, 0);
			return index < 0 ? bytes.Length : index;
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
			string path, string method, T body, NetworkCredential credentials = null, string extra = null) {
			credentials = credentials ?? _defaultCredentials;
			var request = CreateRequest(path, extra, method, "application/vnd.eventstore.events+json", credentials);
			request.Content = new ByteArrayContent(ToJsonBytes(body)) {
				Headers = { ContentType = new MediaTypeHeaderValue("application/vnd.eventstore.events+json") }
			};
			return request;
		}

		protected HttpRequestMessage CreateRawJsonPostRequest<T>(
			string path, string method, T body, NetworkCredential credentials = null, string extra = null) {
			credentials ??= _defaultCredentials;
			var request = CreateRequest(path, extra, method, "application/json", credentials);
			request.Content = new ByteArrayContent(ToJsonBytes(body)) {
				Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
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

		private static Func<HttpResponseMessage, byte[]> CreateDumpResponse() {
			var r = Expression.Parameter(typeof(HttpResponseMessage), "r");
			var piCoreResponseData = typeof(HttpResponseMessage).GetProperty(
				"CoreResponseData",
				BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy |
				BindingFlags.Instance);
			var fim_ConnectStream = piCoreResponseData.PropertyType.GetField("m_ConnectStream",
				BindingFlags.GetField | BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
			var connectStreamType = AppDomain.CurrentDomain.GetAssemblies()
				.Select(a => a.GetType("System.Net.ConnectStream")).Where(t => t != null).FirstOrDefault();
			var fim_ReadBuffer = connectStreamType.GetField("m_ReadBuffer",
				BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
			var body = Expression.Field(
				Expression.Convert(Expression.Field(Expression.Property(r, piCoreResponseData), fim_ConnectStream),
					connectStreamType), fim_ReadBuffer);
			var debugExpression = Expression.Lambda<Func<HttpResponseMessage, byte[]>>(body, r);
			return debugExpression.Compile();
		}

		private static Func<HttpResponseMessage, int> CreateDumpResponse2() {
			var r = Expression.Parameter(typeof(HttpResponseMessage), "r");
			var piCoreResponseData = typeof(HttpResponseMessage).GetProperty(
				"CoreResponseData",
				BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy |
				BindingFlags.Instance);
			var fim_ConnectStream = piCoreResponseData.PropertyType.GetField("m_ConnectStream",
				BindingFlags.GetField | BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
			var connectStreamType = AppDomain.CurrentDomain.GetAssemblies()
				.Select(a => a.GetType("System.Net.ConnectStream")).Where(t => t != null).FirstOrDefault();
			var fim_ReadOffset = connectStreamType.GetField("m_ReadOffset",
				BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
			var fim_ReadBufferSize = connectStreamType.GetField("m_ReadBufferSize",
				BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);
			var stream =
				Expression.Convert(Expression.Field(Expression.Property(r, piCoreResponseData), fim_ConnectStream),
					connectStreamType);
			var body = Expression.Add(Expression.Field(stream, fim_ReadOffset),
				Expression.Field(stream, fim_ReadBufferSize));
			var debugExpression = Expression.Lambda<Func<HttpResponseMessage, int>>(body, r);
			return debugExpression.Compile();
		}

		private static Func<HttpRequestMessage, byte[]> CreateDumpRequest() {
			var r = Expression.Parameter(typeof(HttpRequestMessage), "r");
			var fi_WriteBuffer = typeof(HttpRequestMessage).GetField("_WriteBuffer",
				BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy |
				BindingFlags.Instance);
			var body = Expression.Field(r, fi_WriteBuffer);
			var debugExpression = Expression.Lambda<Func<HttpRequestMessage, byte[]>>(body, r);
			return debugExpression.Compile();
		}

		private static Func<HttpRequestMessage, byte[]> CreateDumpRequest2() {
			var r = Expression.Parameter(typeof(HttpRequestMessage), "r");
			var fi_SubmitWriteStream = typeof(HttpRequestMessage).GetField(
				"_SubmitWriteStream",
				BindingFlags.GetField | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
				| BindingFlags.Instance);
			var connectStreamType = AppDomain.CurrentDomain.GetAssemblies()
				.Select(a => a.GetType("System.Net.ConnectStream")).Where(t => t != null).FirstOrDefault();
			var piBufferedData = connectStreamType.GetProperty(
				"BufferedData",
				BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy
				| BindingFlags.Instance);
			var fiheadChunk = piBufferedData.PropertyType.GetField(
				"headChunk",
				BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy
				| BindingFlags.Instance);
			var piBuffer = fiheadChunk.FieldType.GetField(
				"Buffer",
				BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy
				| BindingFlags.Instance);
			var submitWriteStreamExpression = Expression.Field(r, fi_SubmitWriteStream);
			var headChunk =
				Expression.Condition(
					Expression.ReferenceNotEqual(submitWriteStreamExpression,
						Expression.Constant(null, submitWriteStreamExpression.Type)),
					Expression.Field(
						Expression.Property(
							Expression.Convert(submitWriteStreamExpression, connectStreamType), piBufferedData),
						fiheadChunk),
					Expression.Constant(null, fiheadChunk.FieldType));
			var body =
				Expression.Condition(
					Expression.ReferenceNotEqual(headChunk, Expression.Constant(null, headChunk.Type)),
					Expression.Field(headChunk, piBuffer), Expression.Constant(null, piBuffer.FieldType));
			var debugExpression = Expression.Lambda<Func<HttpRequestMessage, byte[]>>(body, r);
			return debugExpression.Compile();
		}
	}
}
