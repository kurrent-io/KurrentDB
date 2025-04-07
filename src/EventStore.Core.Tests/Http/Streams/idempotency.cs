// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Http.Users.users;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.Streams {
	namespace idempotency {
		[SetUpFixture]
		abstract class HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId>
			: with_admin_user<TLogFormat, TStreamId> {
			protected HttpResponseMessage _response;

			[OneTimeSetUp]
			public override Task TestFixtureSetUp() {
				return base.TestFixtureSetUp();
			}

			[OneTimeTearDown]
			public override Task TestFixtureTearDown() {
				_response?.Dispose();

				return base.TestFixtureTearDown();
			}

			[Test]
			public void response_should_not_be_null() {
				Assert.IsNotNull(_response);
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
			public void returns_a_location_header_ending_with_zero() {
				var location = _response.Headers.GetLocationAsString();
				var tail = location.Substring(location.Length - "/0".Length);
				Assert.AreEqual("/0", tail);
			}

			[Test]
			public async Task returns_a_location_header_that_can_be_read_as_json() {
				var json = await GetJson<JObject>(_response.Headers.GetLocationAsString());
				HelperExtensions.AssertJson(new { A = "1" }, json);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_to_idempotent_guid_id_then_as_array<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override Task Given() {
				_eventId = Guid.NewGuid();
				return PostEvent();
			}

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream + "/incoming/" + _eventId.ToString(), "", "POST",
					"application/json");
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_to_idempotent_guid_id_twice<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override Task Given() {
				_eventId = Guid.NewGuid();
				return PostEvent();
			}

			protected override Task When() {
				return PostEvent();
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream + "/incoming/" + _eventId.ToString(), "", "POST",
					"application/json");
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}


		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_to_idempotent_guid_id_three_times<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override async Task Given() {
				_eventId = Guid.NewGuid();
				await PostEvent();
				await PostEvent();
			}

			protected override Task When() {
				return PostEvent();
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream + "/incoming/" + _eventId.ToString(), "", "POST",
					"application/json");
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}


		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_an_event_once_raw_once_with_array<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override Task Given() {
				_eventId = Guid.NewGuid();
				return PostEvent();
			}

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream, "", "POST", "application/json");
				request.Headers.Add("ES-EventId", _eventId.ToString());
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}


		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_an_event_twice_raw<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override Task Given() {
				_eventId = Guid.NewGuid();
				return PostEvent();
			}

			protected override Task When() {
				return PostEvent();
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream, "", "POST", "application/json");
				request.Headers.Add("ES-EventId", _eventId.ToString());
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_an_event_three_times_raw<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override async Task Given() {
				_eventId = Guid.NewGuid();
				await PostEvent();
				await PostEvent();
			}

			protected override Task When() {
				return PostEvent();
			}

			private async Task PostEvent() {
				var request = CreateRequest(TestStream, "", "POST", "application/json");
				request.Headers.Add("ES-EventId", _eventId.ToString());
				request.Headers.Add("ES-EventType", "SomeType");
				var data = "{a : \"1\"}";
				var bytes = Encoding.UTF8.GetBytes(data);
				request.Content = new ByteArrayContent(bytes) {
					Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
				};
				_response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_an_event_twice_array<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override async Task Given() {
				_eventId = Guid.NewGuid();
				var response1 = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
				Assert.AreEqual(HttpStatusCode.Created, response1.StatusCode);
			}

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
			}
		}


		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_posting_an_event_three_times_as_array<TLogFormat, TStreamId>
			: HttpBehaviorSpecificationOfSuccessfulCreateEvent<TLogFormat, TStreamId> {
			private Guid _eventId;

			protected override async Task Given() {
				_eventId = Guid.NewGuid();
				var response1 = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
				Assert.AreEqual(HttpStatusCode.Created, response1.StatusCode);
				var response2 = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
				Assert.AreEqual(HttpStatusCode.Created, response2.StatusCode);
			}

			protected override async Task When() {
				_response = await MakeArrayEventsPost(
					TestStream,
					new[] { new { EventId = _eventId, EventType = "event-type", Data = new { A = "1" } } });
			}
		}
	}
}
