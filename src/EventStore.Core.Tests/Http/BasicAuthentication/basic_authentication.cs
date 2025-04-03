// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.Core.Services;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using EventStore.Core.Tests.Http.Users.users;

namespace EventStore.Core.Tests.Http.BasicAuthentication {
	namespace basic_authentication {
		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_an_unprotected_resource<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override Task Given() => Task.CompletedTask;
			protected override async Task When() {
				SetDefaultCredentials(null);
				await GetJson<JObject>("/test-anonymous");
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void does_not_return_www_authenticate_header() {
				Assert.IsEmpty(_lastResponse.Headers.WwwAuthenticate);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_a_protected_resource<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				SetDefaultCredentials(null);
				await GetJson<JObject>("/test1");
			}

			[Test]
			public void returns_unauthorized_status_code() {
				Assert.AreEqual(HttpStatusCode.Unauthorized, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_www_authenticate_header() {
				Assert.NotNull(_lastResponse.Headers.WwwAuthenticate);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_a_protected_resource_with_credentials_provided<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override async Task Given() {
				var response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			protected override async Task When() {
				await GetJson<JObject>("/test1", credentials: new NetworkCredential("test1", "Pa55w0rd!"));
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_a_protected_resource_with_invalid_credentials_provided<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override async Task Given() {
				var response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			protected override async Task When() {
				await GetJson<JObject>("/test1", credentials: new NetworkCredential("test1", "InvalidPassword!"));
			}

			[Test]
			public void returns_unauthorized_status_code() {
				Assert.AreEqual(HttpStatusCode.Unauthorized, _lastResponse.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_a_protected_resource_with_credentials_of_disabled_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override async Task Given() {
				var response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
				response = await MakePost("/users/test1/command/disable", _admin);
				Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
			}

			protected override async Task When() {
				await GetJson<JObject>("/test1", credentials: new NetworkCredential("test1", "Pa55w0rd!"));
			}

			[Test]
			public void returns_unauthorized_status_code() {
				Assert.AreEqual(HttpStatusCode.Unauthorized, _lastResponse.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_requesting_a_protected_resource_with_credentials_of_deleted_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override async Task Given() {
				var response = await MakeRawJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
				Console.WriteLine("done with json post");
				response = await MakeDelete("/users/test1", _admin);
				Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
			}

			protected override async Task When() {
				await GetJson<JObject>("/test1", credentials: new NetworkCredential("test1", "Pa55w0rd!"));
			}

			[Test]
			public void returns_unauthorized_status_code() {
				Assert.AreEqual(HttpStatusCode.Unauthorized, _lastResponse.StatusCode);
			}
		}
	}
}
