// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Authentication;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using EventStore.ClientAPI.Common.Utils;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Tests.Services.UserManagementService;

public static class user_management_service {
	public abstract class TestFixtureWithUserManagementService<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		protected Core.Authentication.InternalAuthentication.UserManagementService _users;
		protected readonly ClaimsPrincipal _ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity(
				new [] {
					new Claim(ClaimTypes.Name,"user1"),
					new Claim(ClaimTypes.Role,"role1")
				}
				, "ES-Test"));

		protected override void Given() {
			base.Given();
			NoStream("$user-user1");
			NoStream("$user-user2");
			NoStream("$user-user3");
			NoOtherStreams();
			AllWritesSucceed();

			_users = new Core.Authentication.InternalAuthentication.UserManagementService(
				_ioDispatcher, new StubPasswordHashAlgorithm(), skipInitializeStandardUsersCheck: true, 
				new TaskCompletionSource<bool>(), DefaultData.DefaultUserOptions);

			_bus.Subscribe<UserManagementMessage.Get>(_users);
			_bus.Subscribe<UserManagementMessage.GetAll>(_users);
			_bus.Subscribe<UserManagementMessage.Create>(_users);
			_bus.Subscribe<UserManagementMessage.Update>(_users);
			_bus.Subscribe<UserManagementMessage.Enable>(_users);
			_bus.Subscribe<UserManagementMessage.Disable>(_users);
			_bus.Subscribe<UserManagementMessage.ResetPassword>(_users);
			_bus.Subscribe<UserManagementMessage.ChangePassword>(_users);
			_bus.Subscribe<UserManagementMessage.Delete>(_users);
			_bus.Subscribe<SystemMessage.BecomeLeader>(_users);
			_bus.Subscribe<SystemMessage.BecomeFollower>(_users);
			_bus.Subscribe<SystemMessage.BecomeReadOnlyReplica>(_users);
		}

		protected override ManualQueue GiveInputQueue() {
			return new ManualQueue(_bus, _timeProvider);
		}

		[SetUp]
		public void SetUp() {
			WhenLoop(GivenCommands());
			_queue.Process();
			HandledMessages.Clear();
			WhenLoop();
		}

		protected virtual IEnumerable<WhenStep> GivenCommands() {
			yield break;
		}

		protected ClientMessage.WriteEvents[] HandledPasswordChangedNotificationWrites() {
			return HandledMessages.OfType<ClientMessage.WriteEvents>()
				.Where(
					v =>
						v.EventStreamId
						== Core.Authentication.InternalAuthentication.UserManagementService
							.UserPasswordNotificationsStreamId).ToArray();
		}

		protected ClientMessage.WriteEvents[] HandledPasswordChangedNotificationMetaStreamWrites() {
			return
				HandledMessages.OfType<ClientMessage.WriteEvents>()
					.Where(
						v =>
							v.EventStreamId
							== SystemStreams.MetastreamOf(
								Core.Authentication.InternalAuthentication.UserManagementService
									.UserPasswordNotificationsStreamId))
					.ToArray();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_creating_a_user<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void creates_an_enabled_user_account_with_correct_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("John Doe", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.NotNull(user.Data.Groups);
			Assert.That(user.Data.Groups.Any(v => v == "admin"));
			Assert.That(user.Data.Groups.Any(v => v == "other"));
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void creates_an_enabled_user_account_with_the_correct_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "new-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_ordinary_user_attempts_to_create_a_user<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Create(
					Envelope, _ordinaryUser, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void does_not_create_a_user_account() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.IsFalse(user.Success);
			Assert.AreEqual(UserManagementMessage.Error.NotFound, user.Error);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_creating_an_already_existing_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "Existing John", new[] {"admin", "other"},
					"existing!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"bad"}, "Johny123!");
		}

		[Test]
		public void replies_conflict() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Conflict, updateResults[0].Error);
		}

		[Test]
		public void does_not_override_user_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("Existing John", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.NotNull(user.Data.Groups);
			Assert.That(user.Data.Groups.Any(v => v == "admin"));
			Assert.That(user.Data.Groups.Any(v => v == "other"));
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_override_user_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "existing!", "new-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_updating_user_details<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Update(
					Envelope, SystemAccounts.System, "user1", "Doe John", new[] {"good"});
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void updates_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("Doe John", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.NotNull(user.Data.Groups);
			Assert.That(user.Data.Groups.All(v => v != "admin"));
			Assert.That(user.Data.Groups.All(v => v != "other"));
			Assert.That(user.Data.Groups.Any(v => v == "good"));
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_update_enabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_change_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "new-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_ordinary_user_attempts_to_update_its_own_details<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Update(Envelope, _ordinaryUser, "user1", "Doe John", new[] {"good"});
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void details_are_not_changed() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("John Doe", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.NotNull(user.Data.Groups);
			Assert.That(user.Data.Groups.Any(v => v == "admin"));
			Assert.That(user.Data.Groups.Any(v => v == "other"));
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_update_enabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_change_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "new-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_updating_non_existing_user_details<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield break;
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Update(
					Envelope, SystemAccounts.System, "user1", "Doe John", new[] {"admin", "other"});
		}

		[Test]
		public void replies_not_found() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.NotFound, updateResults[0].Error);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void does_not_create_a_user_account() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.IsFalse(user.Success);
			Assert.AreEqual(UserManagementMessage.Error.NotFound, user.Error);
			Assert.Null(user.Data);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_updating_a_disabled_user_account_details<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
			yield return new UserManagementMessage.Disable(replyTo, SystemAccounts.System, "user1");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.Update(
					Envelope, SystemAccounts.System, "user1", "Doe John", new[] {"good"});
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void does_not_update_enabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(true, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_disabling_an_enabled_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Disable(Envelope, SystemAccounts.System, "user1");
		}

		[Test]
		public void replies_success_with_correct_login_name_set() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void disables_user_account() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(true, user.Data.Disabled);
		}

		[Test]
		public void writes_password_changed_event() {
			var writePasswordChanged = HandledPasswordChangedNotificationWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_an_ordinary_user_attempts_to_disable_a_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Disable(Envelope, _ordinaryUser, "user1");
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void user_account_is_not_disabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(false, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_disabling_a_disabled_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
			yield return new UserManagementMessage.Disable(replyTo, SystemAccounts.System, "user1");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Disable(Envelope, SystemAccounts.System, "user1");
		}

		[Test]
		public void replies_success_with_correct_login_name_set() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void keeps_disabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(true, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_enabling_a_disabled_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
			yield return new UserManagementMessage.Disable(replyTo, SystemAccounts.System, "user1");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Enable(Envelope, SystemAccounts.System, "user1");
		}

		[Test]
		public void replies_success_with_correct_login_name_set() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void enables_user_account() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(false, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_an_ordinary_user_attempts_to_enable_a_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
			yield return new UserManagementMessage.Disable(replyTo, SystemAccounts.System, "user1");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Enable(Envelope, _ordinaryUser, "user1");
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void user_account_is_not_enabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(true, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_enabling_an_enabled_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Enable(Envelope, SystemAccounts.System, "user1");
		}

		[Test]
		public void replies_success_with_correct_login_name_set() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void keeps_enabled() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual(false, user.Data.Disabled);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_resetting_the_password<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.ResetPassword(Envelope, SystemAccounts.System, "user1", "new-password");
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void does_not_update_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("John Doe", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void changes_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "new-password", "other-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}

		[Test]
		public void writes_password_changed_event() {
			var writePasswordChanged = HandledPasswordChangedNotificationWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_resetting_the_password_twice<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.ResetPassword(replyTo, SystemAccounts.System, "user1", "new-password");
			yield return
				new UserManagementMessage.ResetPassword(replyTo, SystemAccounts.System, "user1", "new-password");
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(2, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
			Assert.IsTrue(updateResults[1].Success);
		}

		[Test]
		public void configures_password_changed_notification_system_stream_only_once() {
			var writePasswordChanged = HandledPasswordChangedNotificationMetaStreamWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
			var passwordChangedEvent = writePasswordChanged[0].Events.Single();
			HelperExtensions.AssertJson(new {___maxAge = 3600}, passwordChangedEvent.Data.ParseJson<JObject>());
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_ordinary_user_attempts_to_reset_its_own_password<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.ResetPassword(Envelope, _ordinaryUser, "user1", "new-password");
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void password_is_not_changed() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "other-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_changing_a_password_with_correct_current_password<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "new-password");
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void does_not_update_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("John Doe", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void changes_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "new-password", "other-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}

		[Test]
		public void writes_password_changed_event() {
			var writePasswordChanged = HandledPasswordChangedNotificationWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
		}

		[Test]
		public void configures_password_changed_notification_system_stream() {
			var writePasswordChanged = HandledPasswordChangedNotificationMetaStreamWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
			var passwordChangedEvent = writePasswordChanged[0].Events.Single();
			HelperExtensions.AssertJson(new {___maxAge = 3600}, passwordChangedEvent.Data.ParseJson<JObject>());
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_changing_a_password_with_incorrect_current_password<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "incorrect", "new-password");
		}

		[Test]
		public void replies_unauthorized() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsFalse(updateResults[0].Success);
			Assert.AreEqual(UserManagementMessage.Error.Unauthorized, updateResults[0].Error);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void does_not_update_details() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.AreEqual("John Doe", user.Data.FullName);
			Assert.AreEqual("user1", user.Data.LoginName);
			Assert.AreEqual(false, user.Data.Disabled);
		}

		[Test]
		public void does_not_change_the_password() {
			HandledMessages.Clear();
			_users.Handle(
				new UserManagementMessage.ChangePassword(
					Envelope, SystemAccounts.System, "user1", "Johny123!", "other-password"));
			_queue.Process();
			var updateResult = HandledMessages.OfType<UserManagementMessage.UpdateResult>().Last();
			Assert.NotNull(updateResult);
			Assert.IsTrue(updateResult.Success);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_deleting_an_existing_user_account<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			yield return
				new UserManagementMessage.Create(
					Envelope, SystemAccounts.System, "user1", "John Doe", new[] {"admin", "other"}, "Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.Delete(Envelope, SystemAccounts.System, "user1");
		}

		[Test]
		public void replies_success() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.IsTrue(updateResults[0].Success);
		}

		[Test]
		public void reply_has_the_correct_login_name() {
			var updateResults = HandledMessages.OfType<UserManagementMessage.UpdateResult>().ToList();
			Assert.AreEqual(1, updateResults.Count);
			Assert.AreEqual("user1", updateResults[0].LoginName);
		}

		[Test]
		public void deletes_the_user_account() {
			_users.Handle(new UserManagementMessage.Get(Envelope, SystemAccounts.System, "user1"));
			_queue.Process();
			var user = HandledMessages.OfType<UserManagementMessage.UserDetailsResult>().SingleOrDefault();
			Assert.NotNull(user);
			Assert.IsFalse(user.Success);
			Assert.AreEqual(UserManagementMessage.Error.NotFound, user.Error);
			Assert.Null(user.Data);
		}

		[Test]
		public void writes_password_changed_event() {
			var writePasswordChanged = HandledPasswordChangedNotificationWrites();
			Assert.AreEqual(1, writePasswordChanged.Length);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_getting_all_users<TLogFormat, TStreamId> : TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
		protected override IEnumerable<WhenStep> GivenCommands() {
			var replyTo = Envelope;
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user1", "John Doe 1", new[] {"admin1", "other"},
					"Johny123!");
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user2", "John Doe 2", new[] {"admin2", "other"},
					"Johny123!");
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "user3", "Another Doe 1", new[] {"admin3", "other"},
					"Johny123!");
			yield return
				new UserManagementMessage.Create(
					replyTo, SystemAccounts.System, "another_user", "Another Doe 2", new[] {"admin4", "other"},
					"Johny123!");
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new UserManagementMessage.GetAll(Envelope, SystemAccounts.System);
		}

		[Test]
		public void returns_all_user_accounts() {
			var users = HandledMessages.OfType<UserManagementMessage.AllUserDetailsResult>().Single().Data;

			Assert.AreEqual(4, users.Length);
		}

		[Test]
		public void returns_in_the_login_name_order() {
			var users = HandledMessages.OfType<UserManagementMessage.AllUserDetailsResult>().Single().Data;

			Assert.That(
				new[] {"another_user", "user1", "user2", "user3"}.SequenceEqual(users.Select(v => v.LoginName)));
		}

		[Test]
		public void returns_full_names() {
			var users = HandledMessages.OfType<UserManagementMessage.AllUserDetailsResult>().Single().Data;

			Assert.That(users.Any(v => v.LoginName == "user2" && v.FullName == "John Doe 2"));
		}
	}
}
