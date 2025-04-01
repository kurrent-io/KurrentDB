// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Authorization.Policy;
using Serilog;
using ILogger = Serilog.ILogger;

namespace EventStore.Auth.Ldaps;

internal class LdapsAuthenticationProvider : AuthenticationProviderBase {
	private static readonly ILogger Logger = Log.ForContext<PolicyEvaluator>();
	private readonly ILogger _logger;
	private readonly LdapsSettings _ldapsSettings;
	private readonly bool _logFailedAuthenticationAttempts;
	private readonly IDictionary<string, string> _ldapGroupDistinguishedNameToRoleNames;
	private readonly LRUCache<string, CachedPrincipalWithTimeout> _userPasswordsCache;
	private readonly TimeSpan _cacheDuration;

	public LdapsAuthenticationProvider(LdapsSettings settings, int cacheSize,
		bool logFailedAuthenticationAttempts)
		: base(
			requiredEntitlements: ["LDAPS_AUTHENTICATION"]) {

		_logger = Logger;
		_ldapsSettings = settings;
		_logFailedAuthenticationAttempts =
			logFailedAuthenticationAttempts; //TODO: log failed authentication attempts
		_userPasswordsCache = new LRUCache<string, CachedPrincipalWithTimeout>(cacheSize);
		_ldapGroupDistinguishedNameToRoleNames = settings.LdapGroupRoles;
		_cacheDuration = TimeSpan.FromSeconds(_ldapsSettings.PrincipalCacheDurationSec);
		_logFailedAuthenticationAttempts = logFailedAuthenticationAttempts;
	}

	public override void Authenticate(AuthenticationRequest authenticationRequest) {
		CachedPrincipalWithTimeout cached;
		if (_userPasswordsCache.TryGet(authenticationRequest.Name, out cached)) {
			if (cached.ValidUntil > DateTime.UtcNow) {
				AuthenticateFromCache(authenticationRequest, cached);
			} else {
				_userPasswordsCache.Remove(authenticationRequest.Name);
				AuthenticateWithLdapsServer(authenticationRequest);
			}
		} else {
			AuthenticateWithLdapsServer(authenticationRequest);
		}
	}

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() {
		return new [] {
			"Basic"
		};
	}

	public ClaimsPrincipal GetUser(string loginName) {
		CachedPrincipalWithTimeout cached;
		if (_userPasswordsCache.TryGet(loginName, out cached)) {
			return cached.Principal as ClaimsPrincipal;
		}

		return null;
	}

	private void AuthenticateWithLdapsServer(AuthenticationRequest authenticationRequest) {
		var authenticateTask = new Task<LdapAuthenticationResponse>(() => {
			using (var validator = new LdapsCredentialValidator(_ldapsSettings)) {
				LdapInfo ldapInfo;
				if (validator.TryValidateCredentials(authenticationRequest.Name,
					authenticationRequest.SuppliedPassword, out ldapInfo))
					return new LdapAuthenticationResponse(authenticationRequest, true, ldapInfo);

				return new LdapAuthenticationResponse(authenticationRequest, false, LdapInfo.Empty);
			}
		});

		authenticateTask.ContinueWith(ant => {
			_logger.Error("Error authenticating with LDAPS server. {0}", ant.Exception);
			ant.Result.Request.Error();
		}, TaskContinuationOptions.OnlyOnFaulted);

		authenticateTask.ContinueWith(ant => {
			if (!ant.Result.IsAuthenticated) {
				if (_logFailedAuthenticationAttempts)
					_logger.Warning("LDAPS Authentication Failed for {id}.", authenticationRequest.Id);
				authenticationRequest.Unauthorized();
				return;
			}

			var principal = CreatePrincipal(ant.Result.LdapInfo);
			CachePrincipal(authenticationRequest.Name, authenticationRequest.SuppliedPassword, principal);

			authenticationRequest.Authenticated(principal);
		}, TaskContinuationOptions.NotOnFaulted);

		authenticateTask.Start();
	}

	private void AuthenticateFromCache(AuthenticationRequest authenticationRequest,
		CachedPrincipalWithTimeout cached) {
		if (authenticationRequest.SuppliedPassword != cached.Password) {
			if (_logFailedAuthenticationAttempts)
				_logger.Warning("LDAPS Authentication Failed for {id}.", authenticationRequest.Id);
			authenticationRequest.Unauthorized();
			_userPasswordsCache.Remove(authenticationRequest.Name);
			return;
		}

		authenticationRequest.Authenticated(cached.Principal);
	}

	private void CachePrincipal(string loginName, string password, ClaimsPrincipal principal) {
		var cachedPrincipal =
			new CachedPrincipalWithTimeout(password, principal, DateTime.UtcNow.Add(_cacheDuration));
		_userPasswordsCache.Put(loginName, cachedPrincipal);
	}

	private ClaimsPrincipal CreatePrincipal(LdapInfo ldapInfo) {
		var claims = new List<Claim> {new Claim(ClaimTypes.Name, ldapInfo.LoginName)};
		if (ldapInfo.GroupDistinguishedNames != null) {
			claims.AddRange(ConvertLdapGroupsToRoleNames(ldapInfo).Select(x => new Claim(ClaimTypes.Role, x)));
		}

		var identity = new ClaimsIdentity(claims, "ES-Legacy");
		var principal = new ClaimsPrincipal(identity);
		return principal;
	}

	private string[] ConvertLdapGroupsToRoleNames(LdapInfo ldapInfo) {
		var roles = new List<string> {ldapInfo.LoginName};
		foreach (var ldapGroupDn in ldapInfo.GroupDistinguishedNames) {
			string roleName;
			if (!_ldapGroupDistinguishedNameToRoleNames.TryGetValue(ldapGroupDn, out roleName))
				continue;
			roles.Add(roleName);
		}

		return roles.ToArray();
	}

	private class LdapAuthenticationResponse {
		public readonly AuthenticationRequest Request;
		public readonly bool IsAuthenticated;
		public readonly LdapInfo LdapInfo;

		public LdapAuthenticationResponse(AuthenticationRequest request, bool isAuthenticated, LdapInfo ldapInfo) {
			Request = request;
			IsAuthenticated = isAuthenticated;
			LdapInfo = ldapInfo;
		}
	}
}
