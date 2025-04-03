// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Services;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using ContentType = EventStore.Transport.Http.ContentType;

namespace EventStore.Core.Tests.Http.StreamSecurity {
	namespace stream_access {
		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_creating_a_secured_stream_by_posting_metadata<TLogFormat, TStreamId> : SpecificationWithUsers<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override async Task When() {
				var metadata =
					(StreamMetadata)
					StreamMetadata.Build()
						.SetMetadataReadRole("admin")
						.SetMetadataWriteRole("admin")
						.SetReadRole("")
						.SetWriteRole("other");
				var jsonMetadata = metadata.AsJsonString();
				_response = await MakeArrayEventsPost(
					TestMetadataStream,
					new[] {
						new {
							EventId = Guid.NewGuid(),
							EventType = SystemEventTypes.StreamMetadata,
							Data = new JRaw(jsonMetadata)
						}
					}, _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}

			[Test]
			public async Task refuses_to_post_event_as_anonymous() {
				var response = await PostEvent(new { Some = "Data" });
				Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
			}

			[Test]
			public async Task accepts_post_event_as_authorized_user() {
				var response = await PostEvent(new { Some = "Data" }, GetCorrectCredentialsFor("user1"));
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			[Test]
			[TestCase(SystemHeaders.TrustedAuth)]
			[TestCase(SystemHeaders.LegacyTrustedAuth)]
			public async Task accepts_post_event_as_authorized_user_by_trusted_auth(string trustedAuthHeader) {
				var uri = MakeUrl(TestStream);

				var request = new HttpRequestMessage(HttpMethod.Post, uri) {
					Headers = { { trustedAuthHeader, "root; admin, other" } },
					Content = new ByteArrayContent(
						new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { Some = "Data" } } }
							.ToJsonBytes()) {
						Headers = {
							ContentType = MediaTypeHeaderValue.Parse(ContentType.EventsJson)
						}
					}
				};
				var response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}
		}
	}
}
