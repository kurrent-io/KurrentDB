// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Authentication.InternalAuthentication;

public class InternalAuthenticationProvider : AuthenticationProviderBase, IHandle<InternalAuthenticationProviderMessages.ResetPasswordCache> {
	static readonly ILogger Logger = Serilog.Log.ForContext<InternalAuthenticationProvider>();

	readonly IODispatcher _ioDispatcher;
	readonly bool _logFailedAuthenticationAttempts;
	readonly PasswordHashAlgorithm _passwordHashAlgorithm;

	readonly LRUCache<string, (string hash, string salt, ClaimsPrincipal principal)> _userPasswordsCache;
	
	readonly TaskCompletionSource<bool> _tcs = new();
	
	public InternalAuthenticationProvider(
		ISubscriber subscriber, IODispatcher ioDispatcher,
		PasswordHashAlgorithm passwordHashAlgorithm,
		int cacheSize, bool logFailedAuthenticationAttempts, 
		ClusterVNodeOptions.DefaultUserOptions defaultUserOptions
	) : base(name: "internal", diagnosticsName: "InternalAuthentication") {
		_ioDispatcher = ioDispatcher;
		_passwordHashAlgorithm = passwordHashAlgorithm;
		_userPasswordsCache = new LRUCache<string, (string, string, ClaimsPrincipal)>("UserPasswords", cacheSize);
		_logFailedAuthenticationAttempts = logFailedAuthenticationAttempts;

		var userManagement = new UserManagementService(
			ioDispatcher: ioDispatcher, 
			passwordHashAlgorithm: _passwordHashAlgorithm, 
			skipInitializeStandardUsersCheck: false, 
			tcs: _tcs, 
			defaultUserOptions: defaultUserOptions
		);
		
		subscriber.Subscribe<UserManagementMessage.Create>(userManagement);
		subscriber.Subscribe<UserManagementMessage.Update>(userManagement);
		subscriber.Subscribe<UserManagementMessage.Enable>(userManagement);
		subscriber.Subscribe<UserManagementMessage.Disable>(userManagement);
		subscriber.Subscribe<UserManagementMessage.Delete>(userManagement);
		subscriber.Subscribe<UserManagementMessage.ResetPassword>(userManagement);
		subscriber.Subscribe<UserManagementMessage.ChangePassword>(userManagement);
		subscriber.Subscribe<UserManagementMessage.Get>(userManagement);
		subscriber.Subscribe<UserManagementMessage.GetAll>(userManagement);
		subscriber.Subscribe<SystemMessage.BecomeLeader>(userManagement);
		subscriber.Subscribe<SystemMessage.BecomeFollower>(userManagement);
		subscriber.Subscribe<SystemMessage.BecomeReadOnlyReplica>(userManagement);
	}

	public void Handle(InternalAuthenticationProviderMessages.ResetPasswordCache message) => 
		_userPasswordsCache.Remove(message.LoginName);

	public override void Authenticate(AuthenticationRequest authenticationRequest) {
		if (_userPasswordsCache.TryGet(authenticationRequest.Name, out var cached))
			AuthenticateCached(authenticationRequest, cached.hash, cached.salt, cached.principal);
		else {
			var userStreamId = $"$user-{authenticationRequest.Name}";
			_ioDispatcher.ReadBackward(
				streamId: userStreamId,
				fromEventNumber: -1,
				maxCount: 1,
				resolveLinks: false,
				principal: SystemAccounts.System,
				handler: new AuthReadResponseHandler(self: this, request: authenticationRequest),
				corrId: Guid.NewGuid()
			);
		}
	}

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() => ["Basic", "UserCertificate"];

	void AuthenticateUncached(AuthenticationRequest authenticationRequest, UserData userData) {
		if (!AuthenticateImpl(authenticationRequest, userData.Hash, userData.Salt)) {
			authenticationRequest.Unauthorized();
			return;
		}

		var principal = CreatePrincipal(userData);
		CachePassword(authenticationRequest.Name, userData.Hash, userData.Salt, principal);
		authenticationRequest.Authenticated(principal);
	}

	static ClaimsPrincipal CreatePrincipal(UserData userData) {
		var claims = userData.Groups
			.Select(role => new Claim(ClaimTypes.Role, role))
			.Prepend(new(ClaimTypes.Name, userData.LoginName))
			.ToList();
		
		return new(new ClaimsIdentity(claims, "ES-Legacy"));
	}

	void CachePassword(string loginName, string hash, string salt, ClaimsPrincipal principal) => 
		_userPasswordsCache.Put(loginName, (hash, salt, principal));

	void AuthenticateCached(AuthenticationRequest authenticationRequest, string passwordHash, string passwordSalt, ClaimsPrincipal principal) {
		if (!AuthenticateImpl(authenticationRequest, passwordHash, passwordSalt)) {
			authenticationRequest.Unauthorized();
			return;
		}

		authenticationRequest.Authenticated(principal);
	}

	bool AuthenticateImpl(AuthenticationRequest authenticationRequest, string passwordHash, string passwordSalt) {
		if (authenticationRequest.HasValidClientCertificate)
			// a valid user certificate was supplied. we only needed to verify if the certificate's user
			// exists and is enabled, which we have.
			return true;

		// otherwise default to password authentication
		if (_passwordHashAlgorithm.Verify(authenticationRequest.SuppliedPassword, passwordHash, passwordSalt)) 
			return true;

		if (_logFailedAuthenticationAttempts)
			Logger.Warning("Authentication Failed for {Id}: {Reason}", authenticationRequest.Id, "Invalid credentials supplied.");

		return false;
	}

	public override Task Initialize() => _tcs.Task;

	class AuthReadResponseHandler(InternalAuthenticationProvider self, AuthenticationRequest request) : IReadStreamEventsBackwardHandler {
		public bool HandlesAlt => true;
		public bool HandlesTimeout => true;

		public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted completed) {
			try {
				if (completed.Result == ReadStreamResult.StreamDeleted ||
				    completed.Result == ReadStreamResult.NoStream ||
				    completed.Result == ReadStreamResult.AccessDenied) {
					if (self._logFailedAuthenticationAttempts)
						Logger.Warning("Authentication Failed for {Id}: {Reason}", request.Id, "Invalid user.");
					request.Unauthorized();
					return;
				}

				if (completed.Result == ReadStreamResult.Error) {
					if (self._logFailedAuthenticationAttempts)
						Logger.Warning("Authentication Failed for {Id}: {Reason}", request.Id, "Unexpected error.");
					request.Error();
					return;
				}

				var userData = completed.Events[0].Event.Data.ParseJson<UserData>();
				if (userData.LoginName != request.Name) {
					request.Error();
					return;
				}

				if (userData.Disabled) {
					if (self._logFailedAuthenticationAttempts)
						Logger.Warning("Authentication Failed for {Id}: {Reason}", request.Id, "The account is disabled.");
					
					request.Unauthorized();
				}
				else {
					self.AuthenticateUncached(request, userData);
				}
			}
			catch {
				request.Unauthorized();
			}
		}

		public void Handle(ClientMessage.NotHandled notHandled) {
			if (self._logFailedAuthenticationAttempts)
				Logger.Warning(
					"Authentication Failed for {Id}: {Reason}. {Description}", 
					request.Id, notHandled.Reason, notHandled.Description
				);
			
			request.NotReady();
		}

		public void Timeout() {
			if (self._logFailedAuthenticationAttempts)
				Logger.Warning("Authentication Failed for {Id}: {Reason}", request.Id, "Timeout.");
			
			request.NotReady();
		}
	}
}
