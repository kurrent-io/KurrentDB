// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Auth.Ldaps;

public class LdapsSettings {
	public string Host { get; set; }
	public int Port { get; set; }
	public bool ValidateServerCertificate { get; set; }
	public bool UseSSL { get; set; }

	public bool AnonymousBind { get; set; }
	public string BindUser { get; set; }
	public string BindPassword { get; set; }

	public string BaseDn { get; set; }
	public string ObjectClass { get; set; }
	public string Filter { get; set; }
	public string GroupMembershipAttribute { get; set; }

	public bool RequireGroupMembership { get; set; }
	public string RequiredGroupDn { get; set; }

	public int PrincipalCacheDurationSec { get; set; }

	public Dictionary<string, string> LdapGroupRoles { get; set; }
	public int LdapOperationTimeout { get; set; }

	public LdapsSettings() {
		Port = 636;
		ObjectClass = "organizationalPerson";
		Filter = "sAMAccountName";
		GroupMembershipAttribute = "memberOf";
		PrincipalCacheDurationSec = 60;
		UseSSL = true;
		LdapOperationTimeout = 5000;
	}
}
