// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Tests.Http.Users {
	namespace users {
		public abstract class with_admin_user : with_admin_user<LogFormat.V2, string>;

		public abstract class with_admin_user<TLogFormat, TStreamId> : HttpBehaviorSpecification<TLogFormat, TStreamId> {
			protected readonly NetworkCredential _admin = DefaultData.AdminNetworkCredentials;

			public with_admin_user() {
				SetDefaultCredentials(_admin);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_creating_a_user<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await MakeJsonPost(
					"/users/",
					new {
						LoginName = "test1",
						FullName = "User Full Name",
						Groups = new[] { "admin", "other" },
						Password = "Pa55w0rd!"
					}, _admin);
			}

			[Test]
			public void returns_created_status_code_and_location() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
				Assert.AreEqual(MakeUrl("/users/test1"), _response.Headers.GetLocationAsString());
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_retrieving_a_user_details<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private JObject _response;

			protected override Task Given() {
				return MakeJsonPost(
					"/users/",
					new {
						LoginName = "test1",
						FullName = "User Full Name",
						Groups = new[] { "admin", "other" },
						Password = "Pa55w0rd!"
					}, _admin);
			}

			protected override async Task When() {
				_response = await GetJson<JObject>("/users/test1");
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_valid_json_data() {
				HelperExtensions.AssertJson(
					new {
						Success = true,
						Error = "Success",
						Data =
							new {
								LoginName = "test1",
								FullName = "User Full Name",
								Groups = new[] { "admin", "other" },
								Disabled = false,
								Password___ = false,
								Links = new[] {
									new {
										Href = "http://" + _node.HttpEndPoint +
											   "/users/test1/command/reset-password",
										Rel = "reset-password"
									},
									new {
										Href = "http://" + _node.HttpEndPoint +
											   "/users/test1/command/change-password",
										Rel = "change-password"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test1",
										Rel = "edit"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test1",
										Rel = "delete"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test1/command/disable",
										Rel = "disable"
									}
								}
							}
					}, _response);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_retrieving_a_disabled_user_details<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private JObject _response;

			protected override async Task Given() {
				await MakeJsonPost(
					"/users/",
					new {
						LoginName = "test2",
						FullName = "User Full Name",
						Groups = new[] { "admin", "other" },
						Password = "Pa55w0rd!"
					}, _admin);

				await MakePost("/users/test2/command/disable", _admin);
			}

			protected override async Task When() {
				_response = await GetJson<JObject>("/users/test2");
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public void returns_valid_json_data_with_enable_link() {
				HelperExtensions.AssertJson(
					new {
						Success = true,
						Error = "Success",
						Data =
							new {
								Links = new[] {
									new {
										Href = "http://" + _node.HttpEndPoint +
											   "/users/test2/command/reset-password",
										Rel = "reset-password"
									},
									new {
										Href = "http://" + _node.HttpEndPoint +
											   "/users/test2/command/change-password",
										Rel = "change-password"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test2",
										Rel = "edit"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test2",
										Rel = "delete"
									},
									new {
										Href = "http://" + _node.HttpEndPoint + "/users/test2/command/enable",
										Rel = "enable"
									}
								}
							}
					}, _response);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_creating_an_already_existing_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override async Task Given() {
				var response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			protected override async Task When() {
				_response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
			}

			[Test]
			public void returns_create_status_code_and_location() {
				Assert.AreEqual(HttpStatusCode.Created, _response.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_creating_an_already_existing_user_account_with_a_different_password<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override async Task Given() {
				var response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
			}

			protected override async Task When() {
				_response = await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "AnotherPa55w0rd!" },
					_admin);
			}

			[Test]
			public void returns_conflict_status_code_and_location() {
				Assert.AreEqual(HttpStatusCode.Conflict, _response.StatusCode);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_disabling_an_enabled_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			protected override Task Given() {
				return MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
			}

			protected override Task When() {
				return MakePost("/users/test1/command/disable", _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _lastResponse.StatusCode);
			}

			[Test]
			public async Task enables_it() {
				var jsonResponse = await GetJson<JObject>("/users/test1");
				HelperExtensions.AssertJson(
					new { Success = true, Error = "Success", Data = new { LoginName = "test1", Disabled = true } },
					jsonResponse);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_enabling_a_disabled_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override async Task Given() {
				await MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
				await MakePost("/users/test1/command/disable", _admin);
			}

			protected override async Task When() {
				_response = await MakePost("/users/test1/command/enable", _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
			}

			[Test]
			public async Task disables_it() {
				var jsonResponse = await GetJson<JObject>("/users/test1");
				HelperExtensions.AssertJson(
					new { Success = true, Error = "Success", Data = new { LoginName = "test1", Disabled = false } },
					jsonResponse);
			}
		}

		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_updating_user_details<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override Task Given() {
				return MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
			}

			protected override async Task When() {
				_response = await MakeRawJsonPut("/users/test1", new { FullName = "Updated Full Name" }, _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
			}

			[Test]
			public async Task updates_full_name() {
				var jsonResponse = await GetJson<JObject>("/users/test1");
				HelperExtensions.AssertJson(
					new { Success = true, Error = "Success", Data = new { FullName = "Updated Full Name" } }, jsonResponse);
			}
		}


		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_resetting_a_password<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override Task Given() {
				return MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
			}

			protected override async Task When() {
				_response = await MakeJsonPost(
					"/users/test1/command/reset-password", new { NewPassword = "NewPassword!" }, _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
			}

			[Test]
			public async Task can_change_password_using_the_new_password() {
				var response = await MakeJsonPost(
					"/users/test1/command/change-password",
					new { CurrentPassword = "NewPassword!", NewPassword = "TheVeryNewPassword!" },
					new NetworkCredential("test1", "NewPassword!"));
				Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
			}
		}


		[Category("LongRunning")]
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		class when_deleting_a_user_account<TLogFormat, TStreamId> : with_admin_user<TLogFormat, TStreamId> {
			private HttpResponseMessage _response;

			protected override Task Given() {
				return MakeJsonPost(
					"/users/", new { LoginName = "test1", FullName = "User Full Name", Password = "Pa55w0rd!" }, _admin);
			}

			protected override async Task When() {
				_response = await MakeDelete("/users/test1", _admin);
			}

			[Test]
			public void returns_ok_status_code() {
				Assert.AreEqual(HttpStatusCode.OK, _response.StatusCode);
			}

			[Test]
			public async Task get_returns_not_found() {
				await GetJson<JObject>("/users/test1");
				Assert.AreEqual(HttpStatusCode.NotFound, _lastResponse.StatusCode);
			}
		}
	}
}
