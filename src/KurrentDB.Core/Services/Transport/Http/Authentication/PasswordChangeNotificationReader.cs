// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Authentication.InternalAuthentication;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.UserManagement;
using Newtonsoft.Json;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Transport.Http.Authentication;

public class PasswordChangeNotificationReader :
	IHandle<SystemMessage.SystemStart>,
	IHandle<SystemMessage.BecomeShutdown> {

	private readonly ILogger _log = Log.ForContext<UserManagementService>();
	private readonly IPublisher _publisher;
	private readonly IODispatcher _ioDispatcher;
	private bool _stopped;

	public PasswordChangeNotificationReader(IPublisher publisher, IODispatcher ioDispatcher) {
		_publisher = publisher;
		_ioDispatcher = ioDispatcher;
	}

	private void Start() {
		_stopped = false;
		_ioDispatcher.ReadBackward(
			UserManagementService.UserPasswordNotificationsStreamId, -1, 1, false, SystemAccounts.System,
			completed => {
				switch (completed.Result) {
					case ReadStreamResult.NoStream:
						ReadNotificationsFrom(0);
						break;
					case ReadStreamResult.Success:
						if (completed.Events is [])
							ReadNotificationsFrom(0);
						else
							ReadNotificationsFrom(completed.Events[0].Event.EventNumber + 1);
						break;
					default:
						throw new($"Failed to initialize password change notification reader. Cannot read {UserManagementService.UserPasswordNotificationsStreamId} Error: {completed.Result}");
				}
			});
	}

	private void ReadNotificationsFrom(long fromEventNumber) {
		if (_stopped)
			return;
		_ioDispatcher.ReadForward(
			UserManagementService.UserPasswordNotificationsStreamId,
			fromEventNumber,
			100,
			false,
			SystemAccounts.System,
			completed => {
				if (_stopped)
					return;
				switch (completed.Result) {
					case ReadStreamResult.AccessDenied:
					case ReadStreamResult.Error:
					case ReadStreamResult.NotModified:
						_log.Error("Failed to read: {stream} completed.Result={e}", UserManagementService.UserPasswordNotificationsStreamId, completed.Result.ToString());
						_ioDispatcher.Delay(TimeSpan.FromSeconds(10), _ => ReadNotificationsFrom(fromEventNumber));
						break;
					case ReadStreamResult.NoStream:
					case ReadStreamResult.StreamDeleted:
						_ioDispatcher.Delay(TimeSpan.FromSeconds(1), _ => ReadNotificationsFrom(0));
						break;
					case ReadStreamResult.Success:
						foreach (var @event in completed.Events) {
							PublishPasswordChangeNotificationFrom(@event);
						}

						if (completed.IsEndOfStream) {
							_ioDispatcher.Delay(TimeSpan.FromSeconds(1), _ => ReadNotificationsFrom(completed.NextEventNumber));
						} else {
							ReadNotificationsFrom(completed.NextEventNumber);
						}

						break;
					default:
						throw new NotSupportedException();
				}
			},
			() => {
				_log.Warning("Timeout reading stream: {stream}. Trying again in 10 seconds.", UserManagementService.UserPasswordNotificationsStreamId);
				_ioDispatcher.Delay(TimeSpan.FromSeconds(10), _ => ReadNotificationsFrom(fromEventNumber));
			},
			Guid.NewGuid());
	}


	private class Notification {
#pragma warning disable 649
		public string LoginName;
#pragma warning restore 649
	}

	private void PublishPasswordChangeNotificationFrom(ResolvedEvent @event) {
		var data = @event.Event.Data;
		try {
			var notification = data.ParseJson<Notification>();
			_publisher.Publish(new InternalAuthenticationProviderMessages.ResetPasswordCache(notification.LoginName));
		} catch (JsonException ex) {
			_log.Error("Failed to de-serialize event #{eventNumber}. Error: '{e}'", @event.OriginalEventNumber, ex.Message);
		}
	}

	public void Handle(SystemMessage.SystemStart message) => Start();

	public void Handle(SystemMessage.BecomeShutdown message) => _stopped = true;
}
