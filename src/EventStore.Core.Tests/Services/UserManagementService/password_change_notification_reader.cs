// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;
using System.Linq;

namespace EventStore.Core.Tests.Services.UserManagementService {
	namespace password_change_notification_reader {
		public abstract class with_password_change_notification_reader<TLogFormat, TStreamId> :
			user_management_service.TestFixtureWithUserManagementService<TLogFormat, TStreamId> {
			protected PasswordChangeNotificationReader _passwordChangeNotificationReader;

			protected override void Given() {
				base.Given();
				_passwordChangeNotificationReader = new PasswordChangeNotificationReader(_bus, _ioDispatcher);
				_bus.Subscribe<SystemMessage.SystemStart>(_passwordChangeNotificationReader);
			}

			protected override IEnumerable<WhenStep> PreWhen() {
				foreach (var m in base.PreWhen()) yield return m;
				yield return new SystemMessage.SystemStart();
				yield return
					new UserManagementMessage.Create(
						Envelope, SystemAccounts.System, "user1", "UserOne", new string[] { }, "password");
			}

			[TearDown]
			public void TearDown() {
				_passwordChangeNotificationReader = null;
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_notification_has_been_written<TLogFormat, TStreamId> : with_password_change_notification_reader<TLogFormat, TStreamId> {
			protected override void Given() {
				base.Given();
				NoOtherStreams();
				AllWritesSucceed();
			}


			protected override IEnumerable<WhenStep> When() {
				yield return
					new UserManagementMessage.ChangePassword(
						Envelope, SystemAccounts.System, "user1", "password", "drowssap");
			}

			//TODO GFY THIS TEST LOOKS LIKE ITS NO LONGER VALID AS THE
			//MESSAGE IS THROUGH A STREAM NOT THROUGH THE MAIN BUS
			// [Test]
			// public void publishes_reset_password_cache()
			// {
			//     Assert.AreEqual(
			//         1, HandledMessages.OfType<InternalAuthenticationProviderMessages.ResetPasswordCache>().Count());
			// }
		}
	}
}
