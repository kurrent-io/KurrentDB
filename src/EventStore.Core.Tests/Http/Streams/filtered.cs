// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Tests.Http.Users.users;
using EventStore.Transport.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Http.Streams;

public class Filtered {
	public abstract class SpecificationWithLongFeed<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
		protected int NumberOfEvents;

		protected override async Task Given() {
			NumberOfEvents = 25;
			for (var i = 0; i < NumberOfEvents; i++) {
				await PostEvent(i, TestStream + "-ignore", "ignore-event-type");
				await PostEvent(i, TestStream + "-filter", "event1-type");
				await PostEvent(i, TestStream + "-filter", "event2-type");
			}
		}

		protected async Task<Uri> PostEvent(int i, string streamId, string eventType) {
			var response = await MakeArrayEventsPost(
				streamId,
				new[] { new { EventId = Guid.NewGuid(), EventType = eventType, Data = new { Number = i } } });
			Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			return response.Headers.Location;
		}

		protected string GetLink(JObject feed, string relation) {
			var rel = (from JObject link in feed["links"]
					   from JProperty attr in link
					   where attr.Name == "relation" && (string)attr.Value == relation
					   select link).SingleOrDefault();
			return (rel == null) ? null : (string)rel["uri"];
		}

		protected List<string> GetEventTypes(JObject feed) =>
			feed["entries"]
				.Select(e => e["summary"].Value<string>())
				.OrderBy(e => e)
				.ToList();

		protected string AllFilteredStream => "/streams/%24all/filtered";

		protected string AllFilteredStreamForward => "/streams/$all/filtered/00000000000000000000000000000000/forward/14";
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_with_invalid_context<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=foo");

		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_with_invalid_type<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=streamid&type=foo");

		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_with_invalid_data<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=streamid&type=prefix");

		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_head<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private JObject _feed;

		protected override async Task When() =>
			_feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=prefix&data=event1-");

		[Test]
		public void returns_ok_status_code() =>
			Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);

		[Test]
		public void contains_a_link_rel_previous() {
			var rel = GetLink(_feed, "previous");
			Assert.IsNotEmpty(rel);
		}

		[Test]
		public void contains_a_link_rel_next() {
			var rel = GetLink(_feed, "next");
			Assert.IsNotEmpty(rel);
		}

		[Test]
		public void contains_a_link_rel_self() {
			var rel = GetLink(_feed, "self");
			Assert.IsNotEmpty(rel);
			Assert.AreEqual(
				MakeUrl(AllFilteredStream),
				new Uri(rel));
		}

		[Test]
		public void contains_a_link_rel_first() {
			var rel = GetLink(_feed, "first");
			Assert.IsNotEmpty(rel);
		}

		[Test]
		public void contains_a_link_rel_last() {
			var rel = GetLink(_feed, "last");
			Assert.IsNotEmpty(rel);
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_events_by_event_type_and_prefix<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=prefix&data=event1-,event2-");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			for (var index = 0; index < 7; index++) {
				Assert.AreEqual("event1-type", _eventTypes[index]);
				Assert.AreEqual("event2-type", _eventTypes[index + 7]);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_events_by_event_type_and_regex<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=regex&data=^.*eventtype1.*$");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event1-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_events_by_stream_id_and_prefix<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: $"context=streamid&type=prefix&data={TestStream}-filter");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_events_by_stream_id_and_regex<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: $"context=streamid&type=regex&data=^.*{TestStream}-filter.*$");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event1-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_backward_feed_events_filtering_system_events<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStream,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "exclude-system-events=true");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.IsFalse(eventType.StartsWith("$"));
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_with_invalid_context<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=foo");


		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_with_invalid_type<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=streamid&type=foo");

		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_with_invalid_data<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		protected override Task When() =>
			GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=streamid&type=prefix");

		[Test]
		public void returns_bad_request_status_code() =>
			Assert.AreEqual(HttpStatusCode.BadRequest, _lastResponse.StatusCode);
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_head<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private JObject _feed;

		protected override async Task When() =>
			_feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=prefix&data=event1-");

		[Test]
		public void returns_ok_status_code() =>
			Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);

		[Test]
		public void contains_a_link_rel_previous() {
			var rel = GetLink(_feed, "previous");
			Assert.IsNotEmpty(rel);
		}


		[Test]
		public void contains_a_link_rel_self() {
			var rel = GetLink(_feed, "self");
			Assert.IsNotEmpty(rel);
			Assert.AreEqual(
				MakeUrl(AllFilteredStream),
				new Uri(rel));
		}

		[Test]
		public void contains_a_link_rel_first() {
			var rel = GetLink(_feed, "first");
			Assert.IsNotEmpty(rel);
			Assert.AreEqual(
				MakeUrl($"{AllFilteredStream}/head/backward/14"),
				new Uri(rel));
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_events_by_event_type_and_prefix<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=prefix&data=event1-,event2-");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			for (var index = 0; index < 7; index++) {
				Assert.AreEqual("event1-type", _eventTypes[index]);
				Assert.AreEqual("event2-type", _eventTypes[index + 7]);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_events_by_event_type_and_regex<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "context=eventtype&type=regex&data=^.*eventtype1.*$");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event1-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_events_by_stream_id_and_prefix<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: $"context=streamid&type=prefix&data={TestStream}-filter");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_events_by_stream_id_and_regex<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: $"context=streamid&type=regex&data=^.*{TestStream}-filter.*$");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.AreEqual("event1-type", eventType);
			}
		}
	}

	[Category("LongRunning")]
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_retrieving_forward_feed_events_filtering_system_events<TLogFormat, TStreamId> : SpecificationWithLongFeed<TLogFormat, TStreamId> {
		private List<string> _eventTypes;

		protected override async Task When() {
			var feed = await GetJson<JObject>(AllFilteredStreamForward,
				ContentType.AtomJson,
				DefaultData.AdminNetworkCredentials,
				extra: "exclude-system-events=true");
			_eventTypes = GetEventTypes(feed);
		}

		[Test]
		public void should_only_contain_filtered_events() {
			foreach (var eventType in _eventTypes) {
				Assert.IsFalse(eventType.StartsWith("$"));
			}
		}
	}
}
