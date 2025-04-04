using System;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http {
	[TestFixture, Category("LongRunning")]
	public class ping_controller_should {
		private readonly IPEndPoint _serverEndPoint;
		private readonly PortableServer _portableServer;

		public ping_controller_should() {
			var port = PortsHelper.GetAvailablePort(IPAddress.Loopback);
			_serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
			_portableServer = new PortableServer(_serverEndPoint);
		}

		[SetUp]
		public void SetUp() {
			_portableServer.SetUp(HttpBootstrap.RegisterPing);
		}

		[TearDown]
		public void TearDown() {
			_portableServer.TearDown();
		}

		[Test]
		public void respond_with_httpmessage_text_message() {
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/ping?format=json");
			Func<HttpResponse, bool> verifier = response =>
				Codec.Json.From<HttpMessage.TextMessage>(response.Body) != null;

			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsTrue(result.Item1, result.Item2);
		}

		[Test]
		public void return_response_in_json_if_requested_by_query_param_and_set_content_type_header() {
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/ping?format=json");
			Func<HttpResponse, bool> verifier = response => string.Equals(
				StripAdditionalAttributes(response.ContentType),
				ContentType.Json,
				StringComparison.InvariantCultureIgnoreCase);

			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsTrue(result.Item1, result.Item2);
		}

		[Test]
		public void return_response_in_xml_if_requested_by_query_param_and_set_content_type_header() {
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/ping?format=xml");
			Func<HttpResponse, bool> verifier = response => string.Equals(
				StripAdditionalAttributes(response.ContentType),
				ContentType.Xml,
				StringComparison.InvariantCultureIgnoreCase);

			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsTrue(result.Item1, result.Item2);
		}

		[Test]
		public void return_response_in_plaintext_if_requested_by_query_param_and_set_content_type_header() {
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/ping?format=text");
			Func<HttpResponse, bool> verifier = response => string.Equals(
				StripAdditionalAttributes(response.ContentType),
				ContentType.PlainText,
				StringComparison.InvariantCultureIgnoreCase);

			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsTrue(result.Item1, result.Item2);
		}

		private string StripAdditionalAttributes(string value) {
			var index = value.IndexOf(';');
			if (index < 0)
				return value;
			else
				return value.Substring(0, index);
		}
	}
}
