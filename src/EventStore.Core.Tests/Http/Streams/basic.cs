// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Specialized;
using System.Text;
using EventStore.Core.Tests.Helpers;
using EventStore.Transport.Http;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Xml.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Services;
using EventStore.Core.Tests.Http.Users.users;
using HttpMethod = EventStore.Transport.Http.HttpMethod;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.Streams {
	namespace basic {
		[Category("LongRunning")]
		[TestFixture(ContentType.AtomJson)]
		[TestFixture(ContentType.LegacyAtomJson)]
		public class when_requesting_a_single_event_in_the_stream_as_atom_json(string contentType) : HttpBehaviorSpecificationWithSingleEvent {
			private JObject _json;

			protected override async Task When() {
				_json = await GetJson<JObject>(TestStream + "/0", accept: contentType);
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_correct_body() {
				HelperExtensions.AssertJson(new { Content = new { Data = new { A = "1" } } }, _json);
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_requesting_a_single_event_in_the_stream_as_atom_xml : HttpBehaviorSpecificationWithSingleEvent {
			private XDocument document;

			protected override async Task When() {
				await Get(TestStream + "/0", "", accept: ContentType.Atom);
				document = XDocument.Parse(_lastResponseBody);
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_correct_body() {
				var val = document.GetEntry()
					.Elements(XDocumentAtomExtensions.AtomNamespace + "content").First()
					.Element("data")
					.Element("a").Value;
				Assert.AreEqual(val, "1");
			}
		}

		[TestFixture]
		public class when_posting_an_event_as_raw_json_without_event_type : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeJsonPost(
					TestStream,
					new { A = "1", B = "3", C = "5" });
			}

			[Test]
			public void returns_bad_request_status_code() {
				Assert.AreEqual(HttpStatusCode.BadRequest, _response.StatusCode);
			}
		}

		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventsJson)]
		public class when_posting_an_event_to_idempotent_uri_as_events_array(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream + "/incoming/" + Guid.NewGuid().ToString(),
					new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { A = "1" } } },
					contentType: contentType);
			}

			[Test]
			public void returns_bad_request_status_code() {
				Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, _response.StatusCode);
			}
		}

		[TestFixture]
		public class when_posting_an_event_as_json_to_idempotent_uri_without_event_type : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeJsonPost(
					TestStream + "/incoming/" + Guid.NewGuid().ToString(),
					new { A = "1", B = "3", C = "5" });
			}

			[Test]
			public void returns_bad_request_status_code() {
				Assert.AreEqual(HttpStatusCode.BadRequest, _response.StatusCode);
			}
		}

		[TestFixture]
		public class when_posting_an_event_in_json_to_idempotent_uri_without_event_id : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/incoming/" + Guid.NewGuid(), "", "POST",
					ContentType.Json);
				request.Headers.Add(SystemHeaders.EventType, "SomeType");
				var data = "{a : \"1\", b:\"3\", c:\"5\" }";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue(ContentType.Json) }
				};
				_response = await GetRequestResponse(request);
			}

			[Test]
			public void returns_created_status_code() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.That(!string.IsNullOrEmpty(_response.Headers.GetLocationAsString()));
			}

			[Test]
			public async Task returns_a_location_header_that_can_be_read_as_json() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString());
				HelperExtensions.AssertJson(new { a = "1", b = "3", c = "5" }, json);
			}
		}

		[TestFixture]
		public class when_posting_an_event_as_raw_json_without_event_id : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream, "", "POST", ContentType.Json);
				request.Headers.Add(SystemHeaders.EventType, "SomeType");
				var data = "{A : \"1\", B:\"3\", C:\"5\" }";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue(ContentType.Json) }
				};
				_response = await GetRequestResponse(request);
			}

			[Test]
			public void returns_redirectkeepverb_status_code() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.That(!string.IsNullOrEmpty(_response.Headers.GetLocationAsString()));
			}

			[Test]
			public void returns_a_to_incoming() {
				Assert.IsTrue(_response.Headers.GetLocationAsString().Contains("/incoming/"));
			}
		}

		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventsJson)]
		public class when_posting_an_event_as_array_with_no_event_type(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = Guid.NewGuid(), Data = new { A = "1" } } },
					contentType: contentType);
			}

			[Test]
			public void returns_bad_request_status_code() {
				Assert.AreEqual(HttpStatusCode.BadRequest, _response.StatusCode);
			}
		}

		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventsJson)]
		public class when_posting_an_event_as_array(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { A = "1" } } },
					contentType: contentType);
			}

			[Test]
			public void returns_created_status_code() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public async Task returns_a_location_header_that_can_be_read_as_json() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString());
				HelperExtensions.AssertJson(new { A = "1" }, json);
			}
		}

		[Category("LongRunning")]
		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventJson)]
		public class when_posting_an_event_as_array_to_stream_with_slash(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/", "", "POST", contentType, null);
				request.Content = new ByteArrayContent(new[]
					{new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}}}.ToJsonBytes()) {
					Headers = { ContentType = new MediaTypeHeaderValue(contentType) }
				};
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void redirect_is_cacheable() {
				Assert.AreEqual(CacheControlHeaderValue.Parse("max-age=31536000, public"),
					_response.Headers.CacheControl);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl(TestStream), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_deleting_to_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/", "", "DELETE", ContentType.Json, null);
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl(TestStream), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_getting_from_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/", "", "GET", ContentType.Json, null);
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl(TestStream), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_getting_from_all_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest("/streams/$all/", "", "GET", ContentType.Json, DefaultData.AdminNetworkCredentials);
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl("/streams/$all"), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_getting_from_encoded_all_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest("/streams/$all/", "", "GET", ContentType.Json, null);
				request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
					GetAuthorizationHeader(DefaultData.AdminNetworkCredentials));
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl("/streams/$all").ToString(), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_posting_an_event_as_array_to_metadata_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/metadata/", "", "POST", ContentType.Json, null);
				request.Content = new ByteArrayContent(
					new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { A = "1" } } }
						.ToJsonBytes()) {
					Headers = { ContentType = new MediaTypeHeaderValue(ContentType.Json) }
				};
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl(TestStream + "/metadata"), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_getting_from_metadata_stream_with_slash : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var request = CreateRequest(TestStream + "/metadata/", "", "GET", ContentType.Json, null);
				request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
					GetAuthorizationHeader(DefaultData.AdminNetworkCredentials));
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_permanent_redirect() {
				Assert.AreEqual(HttpStatusCode.RedirectKeepVerb, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public void returns_a_location_header_that_is_to_stream_without_slash() {
				Assert.AreEqual(MakeUrl(TestStream + "/metadata"), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_posting_an_event_without_EventId_as_array : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeJsonPost(
					TestStream,
					new[] { new { EventType = "event-type", Data = new { A = "1" } } });
			}

			[Test]
			public void returns_bad_request_status_code() {
				Assert.AreEqual(HttpStatusCode.BadRequest, _response.StatusCode);
			}
		}

		[TestFixture(ContentType.EventsJson, ContentType.AtomJson)]
		[TestFixture(ContentType.LegacyEventsJson, ContentType.AtomJson)]
		[TestFixture(ContentType.EventsJson, ContentType.LegacyAtomJson)]
		[TestFixture(ContentType.LegacyEventsJson, ContentType.LegacyAtomJson)]
		public class when_posting_an_event_with_type(string postContentType, string acceptContentType)  : with_admin_user  {
			private HttpResponseMessage _response;
			private readonly JArray _data = JArray.Parse(@"[{
					""eventId"": ""fd0489ed-80a5-4004-ad79-1282f657ee27"",
					""eventType"": ""TestType"",
					""data"": {
						""$type"": ""foo_type"",
						""what"": {
							""$type"": ""bar_type"",
							""e622b9f3-ca43-4cf2-9433-8c59b2cb2d3c"": {
								""$type"": ""baz_type"",
								""another"": ""value""
							}
						}
					},
					""metadata"": {
						""$type"": ""qux_type""
					}
				}]");

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(TestStream, _data, contentType: postContentType);
			}

			[Test]
			public async Task the_json_data_retains_the_type() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString(), extra: "embed=tryharder", accept: acceptContentType);
				HelperExtensions.AssertJson(_data.First()["data"], JObject.Parse(json["content"]["data"].ToString()));
			}

			[Test]
			public async Task the_metadata_retains_the_type() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString(), extra: "embed=tryharder", accept: acceptContentType);
				HelperExtensions.AssertJson(_data.First()["metadata"], JObject.Parse(json["content"]["metadata"].ToString()));
			}
		}

		[Category("LongRunning")]
		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventsJson)]
		public class when_posting_an_event_with_date_time(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] {
						new {
							EventId = Guid.NewGuid(),
							EventType = "event-type",
							Data = new {A = "1987-11-07T00:00:00.000+01:00"}
						},
					},
					contentType: contentType);
			}

			[Test]
			public void returns_created_status_code() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public async Task the_json_data_is_not_mangled() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString());
				HelperExtensions.AssertJson(new { A = "1987-11-07T00:00:00.000+01:00" }, json);
			}
		}

		[Category("LongRunning")]
		[TestFixture(ContentType.EventsJson)]
		[TestFixture(ContentType.LegacyEventsJson)]
		public class when_posting_events_as_array(string contentType) : with_admin_user {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] {
						new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}},
						new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "2"}},
					},
					contentType: contentType);
			}

			[Test]
			public void returns_created_status_code() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			[Test]
			public void returns_a_location_header() {
				Assert.IsNotEmpty(_response.Headers.GetLocationAsString());
			}

			[Test]
			public async Task returns_a_location_header_for_the_first_posted_event() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString());
				HelperExtensions.AssertJson(new { A = "1" }, json);
			}
		}

		public abstract class HttpBehaviorSpecificationWithSingleEvent : with_admin_user {
			protected HttpResponseMessage _response;

			protected override async Task Given() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { A = "1" } } });
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_requesting_a_single_event_that_is_deleted_linkto : HttpSpecificationWithLinkToToDeletedEvents {
			protected override Task When() {
				return Get("/streams/" + LinkedStreamName + "/0", "", ContentType.Json, credentials: DefaultData.AdminNetworkCredentials);
			}

			[Test]
			public void the_event_is_gone() {
				Assert.AreEqual(HttpStatusCode.NotFound, _lastResponse.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_requesting_a_single_event_that_is_maxcount_deleted_linkto : SpecificationWithLinkToToMaxCountDeletedEvents {
			protected override Task When() {
				return Get("/streams/" + LinkedStreamName + "/0", "", ContentType.Json, DefaultData.AdminNetworkCredentials);
			}

			[Test]
			public void the_event_is_gone() {
				Assert.AreEqual(HttpStatusCode.NotFound, _lastResponse.StatusCode);
			}
		}


		[Category("LongRunning")]
		[TestFixture]
		public class when_requesting_a_single_event_in_the_stream_without_an_accept_header  : HttpBehaviorSpecificationWithSingleEvent  {
			private XDocument _xmlDocument;

			protected override async Task When() {
				_xmlDocument = await GetXml(MakeUrl(TestStream + "/0"));
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_correct_body() {
				Assert.AreEqual("1", _xmlDocument.Element("data").Element("a").Value);
			}
		}

		[Category("LongRunning")]
		[TestFixture(ContentType.EventJson)]
		[TestFixture(ContentType.LegacyEventJson)]
		public class when_requesting_a_single_event_in_the_stream_as_events_json(string contentType) : HttpBehaviorSpecificationWithSingleEvent {
			private JObject _json;

			protected override async Task When() {
				_json = await GetJson<JObject>(TestStream + "/0", accept: contentType);
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_correct_body() {
				HelperExtensions.AssertJson(new { Data = new { A = "1" } }, _json);
			}
		}

		[Category("LongRunning")]
		[TestFixture]
		public class when_requesting_a_single_event_in_the_stream_as_json : HttpBehaviorSpecificationWithSingleEvent {
			private JObject _json;

			protected override async Task When() {
				_json = await GetJson<JObject>(TestStream + "/0", accept: ContentType.Json);
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_correct_body() {
				HelperExtensions.AssertJson(new { A = "1" }, _json);
			}
		}

		[Category("LongRunning")]
		[TestFixture(ContentType.Xml)]
		[TestFixture(ContentType.EventXml)]
		public class when_requesting_a_single_event_in_the_stream_as_xml(string contentType) : HttpBehaviorSpecificationWithSingleEvent  {
			protected override Task When() {
				return Get(TestStream + "/0", "", accept: contentType);
			}

			[Test]
			public void request_succeeds() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}
		}

		[TestFixture]
		public class when_requesting_a_single_raw_event_in_the_stream_as_raw : with_admin_user {
			protected HttpResponseMessage _response;
			protected byte[] _data;

			protected override async Task Given() {
				var request = CreateRequest(TestStream, String.Empty, HttpMethod.Post, ContentType.Raw);
				request.Headers.Add(SystemHeaders.EventType, "TestEventType");
				request.Headers.Add(SystemHeaders.EventId, Guid.NewGuid().ToString());
				if (_data == null) {
					var fileData = HelperExtensions.GetFilePathFromAssembly("Resources/es-tile.png");
					_data = File.ReadAllBytes(fileData);
				}
				request.Content = new ByteArrayContent(_data) {
					Headers = { ContentType = new MediaTypeHeaderValue(ContentType.Raw) }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			protected override Task When() {
				return Get(TestStream + "/0", "", ContentType.Raw);
			}

			[Test]
			public void returns_correct_body() {
				Assert.AreEqual(_data, _lastResponseBytes);
			}
		}

		[Category("LongRunning")]
		[TestFixture(-2, HttpStatusCode.NoContent)]
		[TestFixture(1, HttpStatusCode.NoContent)]
		[TestFixture(2, HttpStatusCode.BadRequest)]
		public class when_deleting_with_legacy_expected_version(int expectedVersion, HttpStatusCode expectedResult) : with_admin_user {
			private HttpResponseMessage _response;

			protected override async Task Given() {
				var response = await MakeArrayEventsPost(
					TestStream,
					new[] {
						new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "1"}},
						new {EventId = Guid.NewGuid(), EventType = "event-type", Data = new {A = "2"}},
					},
					contentType: ContentType.EventsJson);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			protected override async Task When() {
				var request = CreateRequest(TestStream, "", "DELETE", ContentType.Json,
					headers: new NameValueCollection{{SystemHeaders.LegacyExpectedVersion, expectedVersion.ToString()}});
				_response = await _client.SendAsync(request);
			}

			[Test]
			public void returns_the_expected_http_status_code() {
				Assert.AreEqual(expectedResult, _response.StatusCode);
			}
		}
	}
}
