// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Tests.ConfigurationReaderTests;

public class LdapsSettings {
	public string Host { get; set; } = null!;
	public int Port { get; set; } = 636;
	public bool ValidateServerCertificate { get; set; }
	public bool UseSSL { get; set; } = true;

	public bool AnonymousBind { get; set; }
	public string BindUser { get; set; } = null!;
	public string BindPassword { get; set; } = null!;

	public string BaseDn { get; set; } = null!;
	public string ObjectClass { get; set; } = "organizationalPerson";
	public string Filter { get; set; } = "sAMAccountName";
	public string GroupMembershipAttribute { get; set; } = "memberOf";

	public bool RequireGroupMembership { get; set; }
	public string RequiredGroupDn { get; set; } = null!;

	public int PrincipalCacheDurationSec { get; set; } = 60;

	public Dictionary<string, string> LdapGroupRoles { get; set; } = null!;
}
