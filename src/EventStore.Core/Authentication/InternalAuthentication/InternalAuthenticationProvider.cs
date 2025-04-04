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
using Microsoft.AspNetCore.Routing;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Authentication.InternalAuthentication {
	public class InternalAuthenticationProvider : IAuthenticationProvider,
		IHandle<InternalAuthenticationProviderMessages.ResetPasswordCache> {
		private static readonly ILogger Log = Serilog.Log.ForContext<InternalAuthenticationProvider>();
		private readonly IODispatcher _ioDispatcher;
		private readonly PasswordHashAlgorithm _passwordHashAlgorithm;
		private readonly bool _logFailedAuthenticationAttempts;
		private readonly LRUCache<string, Tuple<string, ClaimsPrincipal>> _userPasswordsCache;
		private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
		private readonly ClusterVNodeOptions.DefaultUserOptions _defaultUserOptions;

		public InternalAuthenticationProvider(ISubscriber subscriber, IODispatcher ioDispatcher, PasswordHashAlgorithm passwordHashAlgorithm,
			int cacheSize, bool logFailedAuthenticationAttempts, ClusterVNodeOptions.DefaultUserOptions defaultUserOptions) {
			_ioDispatcher = ioDispatcher;
			_passwordHashAlgorithm = passwordHashAlgorithm;
			_userPasswordsCache = new LRUCache<string, Tuple<string, ClaimsPrincipal>>("UserPasswords", cacheSize);
			_logFailedAuthenticationAttempts = logFailedAuthenticationAttempts;
			_defaultUserOptions = defaultUserOptions;

			var userManagement = new UserManagementService(ioDispatcher, _passwordHashAlgorithm,
				skipInitializeStandardUsersCheck: false, _tcs, _defaultUserOptions);
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

		public void Authenticate(AuthenticationRequest authenticationRequest) {
			if (_userPasswordsCache.TryGet(authenticationRequest.Name, out var cached)) {
				AuthenticateWithPassword(authenticationRequest, cached.Item1, cached.Item2);
			} else {
				var userStreamId = "$user-" + authenticationRequest.Name;
				_ioDispatcher.ReadBackward(
					streamId: userStreamId,
					fromEventNumber: -1,
					maxCount: 1,
					resolveLinks: false,
					principal: SystemAccounts.System,
					handler: new AuthReadResponseHandler(this, authenticationRequest),
					corrId: Guid.NewGuid());
			}
		}
		public string Name => "internal";
		public IEnumerable<KeyValuePair<string, string>> GetPublicProperties() => null;

		public void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) {
			//nothing to do
		}

		public IReadOnlyList<string> GetSupportedAuthenticationSchemes() {
			return new[] {
				"Basic"
			};
		}

		private void AuthenticateWithPasswordHash(AuthenticationRequest authenticationRequest, UserData userData) {
			if (!_passwordHashAlgorithm.Verify(authenticationRequest.SuppliedPassword, userData.Hash, userData.Salt)) {
				if (_logFailedAuthenticationAttempts)
					Log.Warning("Authentication Failed for {id}: {reason}", authenticationRequest.Id,
						"Invalid credentials supplied.");
				authenticationRequest.Unauthorized();
				return;
			}

			var principal = CreatePrincipal(userData);
			CachePassword(authenticationRequest.Name, authenticationRequest.SuppliedPassword, principal);
			authenticationRequest.Authenticated(principal);
		}

		private static ClaimsPrincipal CreatePrincipal(UserData userData) {
			var claims = new List<Claim> { new Claim(ClaimTypes.Name, userData.LoginName) };
			if (userData.Groups != null) {
				claims.AddRange(userData.Groups.Select(x => new Claim(ClaimTypes.Role, x)));
			}


			var identity = new ClaimsIdentity(claims, "ES-Legacy");
			var principal = new ClaimsPrincipal(identity);
			return principal;
		}

		private void CachePassword(string loginName, string password, ClaimsPrincipal principal) {
			_userPasswordsCache.Put(loginName, Tuple.Create(password, principal));
		}

		private void AuthenticateWithPassword(AuthenticationRequest authenticationRequest, string correctPassword,
			ClaimsPrincipal principal) {
			if (authenticationRequest.SuppliedPassword != correctPassword) {
				if (_logFailedAuthenticationAttempts)
					Log.Warning("Authentication Failed for {id}: {reason}", authenticationRequest.Id,
						"Invalid credentials supplied.");
				authenticationRequest.Unauthorized();
				return;
			}

			authenticationRequest.Authenticated(principal);
		}

		public void Handle(InternalAuthenticationProviderMessages.ResetPasswordCache message) {
			_userPasswordsCache.Remove(message.LoginName);
		}

		public Task Initialize() {
			return _tcs.Task;
		}

		class AuthReadResponseHandler : IReadStreamEventsBackwardHandler {
			private readonly InternalAuthenticationProvider _self;
			private readonly AuthenticationRequest _authenticationRequest;

			public AuthReadResponseHandler(InternalAuthenticationProvider self, AuthenticationRequest request) {
				_self = self;
				_authenticationRequest = request;
			}

			public bool HandlesAlt => true;
			public bool HandlesTimeout => true;

			public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted completed) {
				try {
					if (completed.Result == ReadStreamResult.StreamDeleted ||
						completed.Result == ReadStreamResult.NoStream ||
						completed.Result == ReadStreamResult.AccessDenied) {
						if (_self._logFailedAuthenticationAttempts)
							Log.Warning("Authentication Failed for {id}: {reason}", _authenticationRequest.Id, "Invalid user.");
						_authenticationRequest.Unauthorized();
						return;
					}

					if (completed.Result == ReadStreamResult.Error) {
						if (_self._logFailedAuthenticationAttempts)
							Log.Warning("Authentication Failed for {id}: {reason}", _authenticationRequest.Id,
								"Unexpected error.");
						_authenticationRequest.Error();
						return;
					}

					var userData = completed.Events[0].Event.Data.ParseJson<UserData>();
					if (userData.LoginName != _authenticationRequest.Name) {
						_authenticationRequest.Error();
						return;
					}

					if (userData.Disabled) {
						if (_self._logFailedAuthenticationAttempts)
							Log.Warning("Authentication Failed for {id}: {reason}", _authenticationRequest.Id,
								"The account is disabled.");
						_authenticationRequest.Unauthorized();
					} else {
						_self.AuthenticateWithPasswordHash(_authenticationRequest, userData);
					}
				} catch {
					_authenticationRequest.Unauthorized();
				}
			}

			public void Handle(ClientMessage.NotHandled notHandled) {
				if (_self._logFailedAuthenticationAttempts)
					Log.Warning("Authentication Failed for {id}: {reason}. {description}",
						_authenticationRequest.Id,
						notHandled.Reason,
						notHandled.Description);
				_authenticationRequest.NotReady();
			}

			public void Timeout() {
				if (_self._logFailedAuthenticationAttempts)
					Log.Warning("Authentication Failed for {id}: {reason}", _authenticationRequest.Id,
						"Timeout.");
				_authenticationRequest.NotReady();
			}
		}
	}
}
