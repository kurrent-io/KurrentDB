// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Novell.Directory.Ldap;
using ILogger = Serilog.ILogger;

namespace EventStore.Auth.Ldaps;

/// <summary>
/// A <see cref="LdapsCredentialValidator"/> can be used to validate user credentials with
/// an LDAP server, connecting over SSL only (since we definitely send credentials
/// over the connection).
/// </summary>
internal class LdapsCredentialValidator : ILdapsCredentialValidator, IDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<LdapsCredentialValidator>();
	private readonly LdapSearchConstraints _ldapSearchConstraints;

	private readonly LdapsSettings _settings;
	private readonly LdapConnection _connection;

	private string LdapServer {
		get { return string.Format("{0}:{1}", _settings.Host, _settings.Port); }
	}

	public LdapsCredentialValidator(LdapsSettings settings) {
		_settings = settings;

		_ldapSearchConstraints = new LdapSearchConstraints {
			BatchSize = 0, TimeLimit = _settings.LdapOperationTimeout, ServerTimeLimit = _settings.LdapOperationTimeout,
			ReferralFollowing = true
		};
		var ldapConstraints = new LdapConstraints {TimeLimit = _settings.LdapOperationTimeout};
		_connection = new LdapConnection {
			SecureSocketLayer = _settings.UseSSL,
			Constraints = ldapConstraints
		};
		
#pragma warning disable CS0618 // Type or member is obsolete
		_connection.UserDefinedServerCertValidationDelegate += ConnectionOnUserDefinedServerCertValidationDelegate;
#pragma warning restore CS0618 // Type or member is obsolete
	}

	private bool ConnectionOnUserDefinedServerCertValidationDelegate
		(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors certificateErrors) {
		if (!_settings.ValidateServerCertificate)
			return true;

		if (certificateErrors == SslPolicyErrors.None)
			return true;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			var certificate2 = new X509Certificate2(certificate);
			return chain.Build(certificate2);
		}

		Log.Error("[LDAP-S: {server}] - Server certificate error: {e}", LdapServer,
			string.Join(",", certificateErrors));
		return false;
	}

	private void ConnectToLdapServer() {
		if (_connection.Connected)
			return;

		Log.Debug("Connecting to LDAP server: {server}", LdapServer);
		_connection.Connect(_settings.Host, _settings.Port);
	}

	private bool TryFindLdapInfoForLoginUsername(string username, out LdapInfo ldapInfo) {
		ldapInfo = LdapInfo.Empty;
		var query = string.Format("(&(objectClass={0})({1}={2}))", EscapeLdapSearchFilter(_settings.ObjectClass),
			EscapeLdapSearchFilter(_settings.Filter), EscapeLdapSearchFilter(username));

		try {
			var search = _connection.Search(_settings.BaseDn, LdapConnection.ScopeSub, query,
				new[] {_settings.GroupMembershipAttribute}, false, _ldapSearchConstraints);

			var entries = new List<LdapEntry>();
			while (search.HasMore())
				entries.Add(search.Next());

			if (entries.Count != 1)
				return false;

			var directoryEntry = entries[0];

			//TODO: Return roles here instead of groups

			var attributeSet = directoryEntry.GetAttribute(_settings.GroupMembershipAttribute);
			ldapInfo = new LdapInfo(username, directoryEntry.Dn,
				attributeSet == null ? new string[0] : attributeSet.StringValueArray);
			return true;
		} catch (LdapException ex) {
			if (ex.ResultCode == LdapException.LdapTimeout || ex.ResultCode == LdapException.TimeLimitExceeded) {
				Log.Error(ex, "[LDAP-S: {server}] - Search operation timed out.", LdapServer);
				Disconnect();
				return false;
			}

			Log.Error(ex, "[LDAP-S: {server}] - Exception during search.", LdapServer);
			Disconnect();
			return false;
		}
	}

	public bool TryValidateCredentials(string username, string password, out LdapInfo ldapUser) {
		ldapUser = LdapInfo.Empty;

		if (string.IsNullOrEmpty(username))
			return false;

		try {
			ConnectToLdapServer();
		} catch (LdapException ex) {
			Log.Error(ex, "Connecting to LDAP server {server} failed.", LdapServer);
			Disconnect();
			return false;
		}

		if (!_settings.AnonymousBind) {
			try {
				_connection.Bind(_settings.BindUser, _settings.BindPassword);
			} catch (LdapException ex) {
				if (ex.ResultCode == LdapException.LdapTimeout ||
				    ex.ResultCode == LdapException.TimeLimitExceeded) {
					Log.Error(ex, "[LDAP-S: {server}] - Bind operation timed out.", LdapServer);
					Disconnect();
					return false;
				}

				if (ex.ResultCode == LdapException.InvalidCredentials) {
					Log.Error(ex, "[LDAP-S: {server}] - Invalid bind credentials specified.", LdapServer);
					Disconnect();
					return false;
				}

				Log.Error(ex, "[LDAP-S: {server}] - Exception during bind.", LdapServer);
				Disconnect();
				return false;
			}
		}

		if (!TryFindLdapInfoForLoginUsername(username, out ldapUser))
			return false;

		if (_settings.RequireGroupMembership && !ldapUser.IsMemberOf(_settings.RequiredGroupDn))
			return false;

		try {
			_connection.Bind(ldapUser.DistinguishedName, password);
			return _connection.Bound;
		} catch (LdapException ex) {
			if (ex.ResultCode == LdapException.InvalidCredentials)
				return false;

			Log.Error(ex, "[LDAP-S: {server}] - Exception during bind.", LdapServer);
			return false;
		} finally {
			Disconnect();
		}
	}

	private void Disconnect() {
		if (_connection != null && _connection.Connected)
			_connection.Disconnect();
	}

	public void Dispose() {
		Disconnect();
	}

	private static string EscapeLdapSearchFilter(string input) {
		// escape values taken from http://msdn.microsoft.com/en-us/library/aa746475.aspx
		var escapedFilterBuilder = new StringBuilder();
		foreach (var character in input) {
			switch (character) {
				case '\\':
					escapedFilterBuilder.Append(@"\5c");
					break;
				case '*':
					escapedFilterBuilder.Append(@"\2a");
					break;
				case '(':
					escapedFilterBuilder.Append(@"\28");
					break;
				case ')':
					escapedFilterBuilder.Append(@"\29");
					break;
				case '\u0000':
					escapedFilterBuilder.Append(@"\00");
					break;
				case '/':
					escapedFilterBuilder.Append(@"\2f");
					break;
				default:
					escapedFilterBuilder.Append(character);
					break;
			}
		}

		return escapedFilterBuilder.ToString();
	}
}
