// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Tests.Http.ArgumentPassing {
	namespace matching {
		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_matching_against_simple_placeholders<TLogFormat, TStreamId> : HttpBehaviorSpecification<TLogFormat, TStreamId> {
			private JObject _response;

			protected override Task Given() => Task.CompletedTask;
			protected override Task When() => Task.CompletedTask;

			[Test]
			[TestCase("1", "1", "2", "2")]
			[TestCase("1", "1", "%41", "A")]
			[TestCase("1", "1", "$", "$")]
			[TestCase("1", "1", "%24", "$")]
			[TestCase("%24", "$", "2", "2")]
			[TestCase("$", "$", "2", "2")]
			[TestCase("$", "$", "йцукен", "йцукен")]
			[TestCase("$", "$", "%D0%B9%D1%86%D1%83%D0%BA%D0%B5%D0%BD", "йцукен")]
			[TestCase("йцукен", "йцукен", "2", "2")]
			[TestCase("%D0%B9%D1%86%D1%83%D0%BA%D0%B5%D0%BD", "йцукен", "2", "2")]
			//            [TestCase("%3F", "?", "2", "2")] // ?
			//            [TestCase("%2F", "/", "2", "2")] // /
			[TestCase("%20", " ", "2", "2")] // space
			[TestCase("%25", "%", "2", "2")] // %
			public async Task returns_ok_status_code(string _a, string _ra, string _b, string _rb) {
				_response = await GetJson<JObject>("/test-encoding/" + _a, extra: "b=" + _b);
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
				HelperExtensions.AssertJson(new { a = _ra, b = _rb }, _response);
			}

			[Test]
			[TestCase("*/*")]
			[TestCase("application/json")]
			[TestCase("application/json,*/*;q=0.1")]
			public async Task can_specify_multple_accepts(string accept) {
				_response = await GetJson<JObject>("/test-encoding/1", accept: accept);
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
				HelperExtensions.AssertJson(new { a = "1" }, _response);
			}
		}
	}
}
