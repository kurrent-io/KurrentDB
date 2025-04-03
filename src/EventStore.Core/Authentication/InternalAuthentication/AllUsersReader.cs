// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;

namespace EventStore.Core.Authentication.InternalAuthentication;

public class AllUsersReader {
	private const string UserEventType = "$User";
	private const string UserStreamPrefix = "$user-";
	private readonly IODispatcher _ioDispatcher;
	private readonly List<UserManagementMessage.UserData> _results = new List<UserManagementMessage.UserData>();
	private Action<UserManagementMessage.Error, UserManagementMessage.UserData[]> _onCompleted;
	private bool _aborted;

	public AllUsersReader(IODispatcher ioDispatcher) {
		_ioDispatcher = ioDispatcher;
	}

	public void Run(Action<UserManagementMessage.Error, UserManagementMessage.UserData[]> completed) {
		if (completed == null) throw new ArgumentNullException(nameof(completed));
		if (_onCompleted != null) throw new InvalidOperationException("AllUsersReader cannot be re-used");

		_onCompleted = completed;

		BeginReadForward(0);
	}

	private void BeginReadForward(long fromEventNumber) {
		_ioDispatcher.ReadForward(
			"$users", fromEventNumber, 1, false, SystemAccounts.System, ReadUsersForwardCompleted);
	}

	private void ReadUsersForwardCompleted(ClientMessage.ReadStreamEventsForwardCompleted result) {
		if (_aborted)
			return;
		switch (result.Result) {
			case ReadStreamResult.Success:
				foreach (var loginName in from eventData in result.Events
					let @event = eventData.Event
					where @event.EventType == UserEventType
					let stringData = Helper.UTF8NoBom.GetString(@event.Data.Span)
					select stringData)
					BeginReadUserDetails(loginName, () => {
						if (!result.IsEndOfStream)
							BeginReadForward(result.NextEventNumber);
						else
							TryComplete();
					});

				break;
			case ReadStreamResult.NoStream:
				Abort(UserManagementMessage.Error.NotFound);
				break;
			default:
				Abort(UserManagementMessage.Error.Error);
				break;
		}
	}

	private void Abort(UserManagementMessage.Error error) {
		_onCompleted(error, null);
		_onCompleted = null;
		_aborted = true;
	}

	private void BeginReadUserDetails(string loginName, Action next) {
		_ioDispatcher.ReadBackward(
			UserStreamPrefix + loginName, -1, 1, false, SystemAccounts.System,
			result => {
				ReadUserDetailsBackwardCompleted(loginName, result);
				next();
			});
	}

	private void ReadUserDetailsBackwardCompleted(
		string loginName, ClientMessage.ReadStreamEventsBackwardCompleted result) {
		if (_aborted)
			return;
		switch (result.Result) {
			case ReadStreamResult.Success:
				if (_results.Any(x => x.LoginName == loginName)) break;
				if (result.Events.Count is not 1) {
					AddLoadedUserDetails(loginName, "", [], true, null);
				} else {
					try {
						var eventRecord = result.Events[0].Event;
						var userData = eventRecord.Data.ParseJson<UserData>();
						AddLoadedUserDetails(
							userData.LoginName, userData.FullName, userData.Groups, userData.Disabled,
							new DateTimeOffset(eventRecord.TimeStamp, TimeSpan.FromHours(0)));
					} catch {
						Abort(UserManagementMessage.Error.Error);
					}
				}

				break;
			case ReadStreamResult.NoStream:
				break;
			case ReadStreamResult.StreamDeleted:
				// ignore - deleted
				break;
			default:
				Abort(UserManagementMessage.Error.Error);
				break;
		}
	}

	private void TryComplete() {
		if (!_aborted)
			_onCompleted(UserManagementMessage.Error.Success, _results.ToArray());
	}

	private void AddLoadedUserDetails(
		string loginName, string fullName, string[] groups, bool disabled, DateTimeOffset? dateLastUpdated) {
		_results.Add(new UserManagementMessage.UserData(loginName, fullName, groups, disabled, dateLastUpdated));
	}
}
