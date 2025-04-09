// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.UserManagement;
using Serilog;
using ILogger = Serilog.ILogger;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Authentication.InternalAuthentication;

[PublicAPI]
public class UserManagementService :
	IHandle<UserManagementMessage.Get>,
	IHandle<UserManagementMessage.GetAll>,
	IHandle<UserManagementMessage.Create>,
	IHandle<UserManagementMessage.Update>,
	IHandle<UserManagementMessage.Enable>,
	IHandle<UserManagementMessage.Disable>,
	IHandle<UserManagementMessage.ResetPassword>,
	IHandle<UserManagementMessage.ChangePassword>,
	IHandle<UserManagementMessage.Delete>,
	IHandle<SystemMessage.BecomeLeader>,
	IHandle<SystemMessage.BecomeFollower>,
	IHandle<SystemMessage.BecomeReadOnlyReplica> {
	static readonly ILogger Logger = Log.ForContext<UserManagementService>();

	public const string UserUpdated = "$UserUpdated";
	public const string PasswordChanged = "$PasswordChanged";
	public const string UserPasswordNotificationsStreamId = "$users-password-notifications";
	public const string UsersStreamType = "$User";

	readonly IODispatcher _ioDispatcher;
	readonly PasswordHashAlgorithm _passwordHashAlgorithm;
	readonly bool _skipInitializeStandardUsersCheck;
	readonly TaskCompletionSource<bool> _tcs;
	int _numberOfStandardUsersToBeCreated = 2;

	readonly ClusterVNodeOptions.DefaultUserOptions _defaultUserOptions;

	public UserManagementService(
		IODispatcher ioDispatcher,
		PasswordHashAlgorithm passwordHashAlgorithm,
		bool skipInitializeStandardUsersCheck,
		TaskCompletionSource<bool> tcs,
		ClusterVNodeOptions.DefaultUserOptions defaultUserOptions
	) {
		_ioDispatcher = ioDispatcher;
		_passwordHashAlgorithm = passwordHashAlgorithm;
		_skipInitializeStandardUsersCheck = skipInitializeStandardUsersCheck;
		_tcs = tcs;
		_defaultUserOptions = defaultUserOptions;
	}

	bool VerifyPassword(string password, UserData userDetailsToVerify) {
		_passwordHashAlgorithm.Hash(password, out _, out _);
		return _passwordHashAlgorithm.Verify(password, userDetailsToVerify.Hash, userDetailsToVerify.Salt);
	}

	public void Handle(UserManagementMessage.Create message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		var userData = CreateUserData(message);
		BeginReadUserDetails(message.LoginName, read => {
			if (read.Events.Count > 0) {
				var data = read.Events[^1].Event.Data.ParseJson<UserData>();
				if (VerifyPassword(message.Password, data)) {
					ReplyUpdated(message);
					return;
				}

				ReplyConflict(message);
				return;
			}

			WriteStreamAcl(
				message, message.LoginName,
				() => WriteUserEventAnd(
					message, userData, "$UserCreated", read.LastEventNumber,
					() => WriteUsersStreamEvent(userData.LoginName, completed => WriteUsersStreamCompleted(completed, message))
				)
			);
		});
	}

	public void Handle(UserManagementMessage.Update message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		ReadUpdateWriteReply(
			message, data => data.SetFullName(message.FullName).SetGroups(message.Groups),
			resetPasswordCache: true
		);
	}

	public void Handle(UserManagementMessage.Enable message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		ReadUpdateWriteReply(message, data => data.SetEnabled(), resetPasswordCache: false);
	}

	public void Handle(UserManagementMessage.Disable message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		ReadUpdateWriteReply(message, data => data.SetDisabled(), resetPasswordCache: true);
	}

	public void Handle(UserManagementMessage.ResetPassword message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		_passwordHashAlgorithm.Hash(message.NewPassword, out var hash, out var salt);
		ReadUpdateWriteReply(message, data => data.SetPassword(hash, salt), resetPasswordCache: true);
	}

	public void Handle(UserManagementMessage.ChangePassword message) {
		_passwordHashAlgorithm.Hash(message.NewPassword, out var hash, out var salt);
		ReadUpdateWriteReply(
			message,
			data => {
				if (_passwordHashAlgorithm.Verify(message.CurrentPassword, data.Hash, data.Salt))
					return data.SetPassword(hash, salt);

				ReplyUnauthorized(message);
				return null;
			},
			resetPasswordCache: true
		);
	}

	public void Handle(UserManagementMessage.Delete message) {
		if (!IsAdmin(message.Principal)) {
			ReplyUnauthorized(message);
			return;
		}

		ReadUpdateCheckAnd(
			message,
			(completed, _) =>
				_ioDispatcher.DeleteStream(
					$"$user-{message.LoginName}",
					completed.FromEventNumber,
					false,
					SystemAccounts.System,
					streamCompleted => WritePasswordChangedEventConditionalAnd(message, true, () => ReplyByWriteResult(message, streamCompleted.Result))
				)
		);
	}

	public void Handle(UserManagementMessage.Get message) {
		ReadUserDetailsAnd(
			message, (completed, data) => {
				if (completed.Result == ReadStreamResult.Success && completed.Events.Count is 1)
					message.Envelope.ReplyWith(
						new UserManagementMessage.UserDetailsResult(
							new UserManagementMessage.UserData(
								message.LoginName, data.FullName, data.Groups, data.Disabled,
								new DateTimeOffset(completed.Events[0].Event.TimeStamp, TimeSpan.FromHours(0)))));
				else {
					switch (completed.Result) {
						case ReadStreamResult.NoStream:
						case ReadStreamResult.StreamDeleted:
							message.Envelope.ReplyWith(
								new UserManagementMessage.UserDetailsResult(
									UserManagementMessage.Error.NotFound));
							break;
						default:
							message.Envelope.ReplyWith(
								new UserManagementMessage.UserDetailsResult(UserManagementMessage.Error.Error));
							break;
					}
				}
			});
	}

	public void Handle(UserManagementMessage.GetAll message) {
		new AllUsersReader(_ioDispatcher).Run(
			(error, data) =>
				message.Envelope.ReplyWith(
					error == UserManagementMessage.Error.Success
						? new(data.OrderBy(v => v.LoginName).ToArray())
						: new UserManagementMessage.AllUserDetailsResult(error)));
	}

	public void Handle(SystemMessage.BecomeLeader message) {
		Interlocked.Exchange(ref _numberOfStandardUsersToBeCreated, 2);
		if (!_skipInitializeStandardUsersCheck) {
			BeginReadUserDetails(
				"admin", completed => {
					if (completed.Result == ReadStreamResult.NoStream)
						CreateAdminUser();
					else
						NotifyInitialized();
				});
			BeginReadUserDetails(
				"ops", completed => {
					if (completed.Result == ReadStreamResult.NoStream)
						CreateOperationsUser();
					else
						NotifyInitialized();
				});
		} else
			_tcs.TrySetResult(true);
	}

	public void Handle(SystemMessage.BecomeFollower message) =>
		_tcs.TrySetResult(true);

	public void Handle(SystemMessage.BecomeReadOnlyReplica message) =>
		_tcs.TrySetResult(true);

	void NotifyInitialized() {
		var remainingUsers = Interlocked.Decrement(ref _numberOfStandardUsersToBeCreated);
		if (remainingUsers == 0)
			_tcs.TrySetResult(true);
	}

	UserData CreateUserData(UserManagementMessage.Create message) =>
		CreateUserData(message.LoginName, message.FullName, message.Groups, message.Password);

	UserData CreateUserData(string loginName, string fullName, string[] groups, string password) {
		_passwordHashAlgorithm.Hash(password, out var hash, out var salt);
		return new(loginName, fullName, groups, hash, salt, disabled: false);
	}

	void ReadUserDetailsAnd(
		UserManagementMessage.UserManagementRequestMessage message,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted, UserData> action
	) {
		BeginReadUserDetails(
			message.LoginName, completed => {
				switch (completed.Result) {
					case ReadStreamResult.NoStream:
						ReplyNotFound(message);
						break;
					case ReadStreamResult.StreamDeleted:
						ReplyNotFound(message);
						break;
					case ReadStreamResult.Success:
						if (completed.Events is [])
							ReplyNotFound(message);
						else {
							var data1 = completed.Events[0].Event.Data.ParseJson<UserData>();
							action(completed, data1);
						}

						break;
					default:
						ReplyInternalError(message);
						break;
				}
			});
	}

	void BeginReadUserDetails(string loginName, Action<ClientMessage.ReadStreamEventsBackwardCompleted> completed) =>
		_ioDispatcher.ReadBackward($"$user-{loginName}", -1, 1, false, SystemAccounts.System, completed);

	void ReadUpdateWriteReply(UserManagementMessage.UserManagementRequestMessage message, Func<UserData, UserData> update, bool resetPasswordCache) {
		ReadUpdateCheckAnd(
			message, (completed, data) => {
				var updated = update(data);
				if (updated is not null) {
					WriteUserEventAnd(message, updated, UserUpdated, completed.FromEventNumber, () =>
						WritePasswordChangedEventConditionalAnd(message, resetPasswordCache, () => ReplyUpdated(message))
					);
				}
			});
	}

	void WritePasswordChangedEventConditionalAnd(
		UserManagementMessage.UserManagementRequestMessage message, bool resetPasswordCache, Action onCompleted) {
		if (resetPasswordCache)
			BeginWritePasswordChangedEvent(
				message.LoginName,
				eventsCompleted => WritePasswordChangedEventCompleted(message, eventsCompleted, onCompleted)
			);
		else
			onCompleted();

		return;

		static void WritePasswordChangedEventCompleted(
			UserManagementMessage.UserManagementRequestMessage message,
			ClientMessage.WriteEventsCompleted eventsCompleted,
			Action onCompleted
		) {
			switch (eventsCompleted.Result) {
				case OperationResult.Success:
					onCompleted();
					break;
				case OperationResult.CommitTimeout:
				case OperationResult.ForwardTimeout:
				case OperationResult.PrepareTimeout:
					ReplyTryAgain(message);
					break;
				case OperationResult.AccessDenied:
					ReplyUnauthorized(message);
					break;
				case OperationResult.WrongExpectedVersion:
					ReplyConflict(message);
					break;
				default:
					ReplyInternalError(message);
					break;
			}
		}
	}

	void BeginWritePasswordChangedEvent(string loginName, Action<ClientMessage.WriteEventsCompleted> completed) {
		var streamMetadata = new Lazy<StreamMetadata>(() => new(null, TimeSpan.FromHours(1)));
		_ioDispatcher.ConfigureStreamAndWriteEvents(
			UserPasswordNotificationsStreamId, ExpectedVersion.Any, streamMetadata,
			[CreatePasswordChangedEvent(loginName)], SystemAccounts.System, completed
		);
	}

	static Event CreatePasswordChangedEvent(string loginName) =>
		new(Guid.NewGuid(), PasswordChanged, true, new { LoginName = loginName }.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

	void ReadUpdateCheckAnd(UserManagementMessage.UserManagementRequestMessage message,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted, UserData> action) {
		ReadUserDetailsAnd(message, action);
	}

	void WriteStreamAcl(UserManagementMessage.UserManagementRequestMessage message, string loginName, Action onSucceeded) {
		WriteStreamAcl(
			loginName, completed => {
				switch (completed.Result) {
					case OperationResult.Success:
						onSucceeded();
						break;
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
					case OperationResult.PrepareTimeout:
					case OperationResult.WrongExpectedVersion:
						ReplyTryAgain(message);
						break;
					default:
						ReplyInternalError(message);
						break;
				}
			});
	}

	void WriteStreamAcl(string loginName, Action<ClientMessage.WriteEventsCompleted> onCompleted) {
		_ioDispatcher.UpdateStreamAcl(
			$"$user-{loginName}", ExpectedVersion.Any, SystemAccounts.System,
			new(
				null, null, null, null, null,
				new(null, SystemRoles.Admins, SystemRoles.Admins, null, SystemRoles.Admins)),
			onCompleted
		);
	}

	void WriteUserEventAnd(UserManagementMessage.UserManagementRequestMessage message, UserData userData, string eventType, long expectedVersion, Action after) {
		WriteUserEvent(
			userData, eventType, expectedVersion,
			completed => WriteUserCreatedCompleted(completed, message, after));
	}

	void WriteUserEvent(UserData userData, string eventType, long expectedVersion, Action<ClientMessage.WriteEventsCompleted> onCompleted) {
		var userCreatedEvent = new Event(Guid.NewGuid(), eventType, true, userData.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);
		_ioDispatcher.WriteEvents($"$user-{userData.LoginName}", expectedVersion, [userCreatedEvent], SystemAccounts.System, onCompleted);
	}

	static void WriteUserCreatedCompleted(ClientMessage.WriteEventsCompleted completed,
		UserManagementMessage.UserManagementRequestMessage message, Action after) {
		if (completed.Result == OperationResult.Success) {
			if (after is not null)
				after();
			else
				ReplyUpdated(message);

			return;
		}

		ReplyByWriteResult(message, completed.Result);
	}

	void WriteUsersStreamEvent(string loginName, Action<ClientMessage.WriteEventsCompleted> onCompleted) {
		var userCreatedEvent = new Event(Guid.NewGuid(), UsersStreamType, false, loginName, null, SchemaInfo.None, SchemaInfo.None);
		_ioDispatcher.WriteEvents("$users", ExpectedVersion.Any, [userCreatedEvent], SystemAccounts.System, onCompleted);
	}

	static void WriteUsersStreamCompleted(ClientMessage.WriteEventsCompleted completed, UserManagementMessage.UserManagementRequestMessage message) {
		if (completed.Result == OperationResult.Success) {
			ReplyUpdated(message);
			return;
		}

		ReplyByWriteResult(message, completed.Result);
	}

	static void ReplyInternalError(UserManagementMessage.UserManagementRequestMessage message) =>
		ReplyError(message, UserManagementMessage.Error.Error);

	static void ReplyNotFound(UserManagementMessage.UserManagementRequestMessage message) =>
		ReplyError(message, UserManagementMessage.Error.NotFound);

	static void ReplyConflict(UserManagementMessage.UserManagementRequestMessage message) =>
		ReplyError(message, UserManagementMessage.Error.Conflict);

	//NOTE: probably unauthorized iis not correct reply here.
	// been converted to http 401 status code it will prompt for authorization
	static void ReplyUnauthorized(UserManagementMessage.UserManagementRequestMessage message) =>
		ReplyError(message, UserManagementMessage.Error.Unauthorized);

	static void ReplyTryAgain(UserManagementMessage.UserManagementRequestMessage message) =>
		ReplyError(message, UserManagementMessage.Error.TryAgain);

	static void ReplyUpdated(UserManagementMessage.UserManagementRequestMessage message) =>
		message.Envelope.ReplyWith(new UserManagementMessage.UpdateResult(message.LoginName));

	static void ReplyError(UserManagementMessage.UserManagementRequestMessage message, UserManagementMessage.Error error) {
		//TODO: avoid 'is'
		if (message is UserManagementMessage.Get)
			message.Envelope.ReplyWith(new UserManagementMessage.UserDetailsResult(error));
		else
			message.Envelope.ReplyWith(new UserManagementMessage.UpdateResult(message.LoginName, error));
	}

	static void ReplyByWriteResult(UserManagementMessage.UserManagementRequestMessage message, OperationResult operationResult) {
		switch (operationResult) {
			case OperationResult.Success:
				ReplyUpdated(message);
				break;
			case OperationResult.PrepareTimeout:
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout:
				ReplyTryAgain(message);
				break;
			case OperationResult.WrongExpectedVersion:
			case OperationResult.StreamDeleted:
				ReplyConflict(message);
				break;
			default:
				ReplyInternalError(message);
				break;
		}
	}

	void CreateAdminUser() {
		var userData = CreateUserData(
			SystemUsers.Admin,
			"KurrentDB Administrator",
			[SystemRoles.Admins],
			_defaultUserOptions.DefaultAdminPassword
		);

		WriteStreamAcl(
			SystemUsers.Admin, completed1 => {
				switch (completed1.Result) {
					case OperationResult.CommitTimeout:
					case OperationResult.PrepareTimeout:
						CreateAdminUser();
						break;
					default:
						Logger.Error("'admin' user account could not be created");
						NotifyInitialized();
						break;
					case OperationResult.Success:
						WriteUserEvent(
							userData, "$UserCreated", ExpectedVersion.NoStream, completed => {
								switch (completed.Result) {
									case OperationResult.Success:
										Logger.Information("'admin' user account has been created.");
										WriteUsersStreamEvent("admin", x => {
											if (x.Result == OperationResult.Success) {
												Logger.Information("'admin' user added to $users.");
											} else {
												Logger.Error("unable to add 'admin' to $users. {OperationResult}", x.Result);
											}

											NotifyInitialized();
										});
										break;
									case OperationResult.CommitTimeout:
									case OperationResult.PrepareTimeout:
										Logger.Error("'admin' user account creation timed out retrying.");
										CreateAdminUser();
										break;
									default:
										Logger.Error("'admin' user account could not be created.");
										NotifyInitialized();
										break;
								}
							});
						break;
				}
			});
	}

	void CreateOperationsUser() {
		var userData = CreateUserData(
			SystemUsers.Operations,
			"KurrentDB Operations",
			[SystemRoles.Operations],
			_defaultUserOptions.DefaultOpsPassword
		);

		WriteStreamAcl(
			SystemUsers.Operations, completed1 => {
				switch (completed1.Result) {
					case OperationResult.CommitTimeout:
					case OperationResult.PrepareTimeout:
						CreateOperationsUser();
						break;
					default:
						Logger.Error("'ops' user account could not be created");
						NotifyInitialized();
						break;
					case OperationResult.Success:
						WriteUserEvent(
							userData, "$UserCreated", ExpectedVersion.NoStream, completed => {
								switch (completed.Result) {
									case OperationResult.Success:
										Logger.Information("'ops' user account has been created.");
										WriteUsersStreamEvent("ops", x => {
											if (x.Result == OperationResult.Success) {
												Logger.Information("'ops' user added to $users.");
											} else {
												Logger.Error("unable to add 'ops' to $users. {OperationResult}", x.Result);
											}

											NotifyInitialized();
										});
										break;
									case OperationResult.CommitTimeout:
									case OperationResult.PrepareTimeout:
										Logger.Error("'ops' user account creation timed out retrying.");
										CreateOperationsUser();
										break;
									default:
										Logger.Error("'ops' user account could not be created.");
										NotifyInitialized();
										break;
								}
							});
						break;
				}
			});
	}

	static bool IsAdmin(ClaimsPrincipal principal) =>
		principal?.LegacyRoleCheck(SystemRoles.Admins) == true;
}
