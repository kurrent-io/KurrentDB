// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

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
			public async Task accepts_post_event_as_authorized_user_by_trusted_auth() {
				var uri = MakeUrl(TestStream);

				var request = new HttpRequestMessage(HttpMethod.Post, uri) {
					Headers = { { "ES-TrustedAuth", "root; admin, other" } },
					Content = new ByteArrayContent(
						new[] { new { EventId = Guid.NewGuid(), EventType = "event-type", Data = new { Some = "Data" } } }
							.ToJsonBytes()) {
						Headers = {
							ContentType = MediaTypeHeaderValue.Parse("application/vnd.eventstore.events+json")
						}
					}
				};
				var response = await GetRequestResponse(request);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}
		}
	}
}
