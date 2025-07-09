// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Tests.ConfigurationReaderTests;

public class when_reading_valid_configuration {
	[Fact]
	public void should_return_correct_options() {
		var settings = ConfigParser.ReadConfiguration<LdapsSettings>(
			Path.Combine("ConfigurationReaderTests", "valid_node_config.yaml"),
			"LdapsAuth"
		);

		settings!.Host.Should().Be("13.64.104.29");
		settings.Port.Should().Be(389);
		settings.ValidateServerCertificate.Should().BeFalse();
		settings.UseSSL.Should().BeFalse();
		settings.AnonymousBind.Should().BeFalse();
		settings.BindUser.Should().Be("mycompany\\binder");
		settings.BindPassword.Should().Be("p@ssw0rd!");
		settings.BaseDn.Should().Be("ou=Lab,dc=mycompany,dc=local");
		settings.ObjectClass.Should().Be("organizationalPerson");
		settings.GroupMembershipAttribute.Should().Be("memberOf");
		settings.RequireGroupMembership.Should().BeFalse();
		settings.RequiredGroupDn.Should().Be("RequiredGroupDn");
		settings.PrincipalCacheDurationSec.Should().Be(120);
		settings.LdapGroupRoles.Should().BeEquivalentTo(new Dictionary<string, string> {
			{ "CN=ES-Accounting,CN=Users,DC=mycompany,DC=local", "accounting" },
			{ "CN=ES-Operations,CN=Users,DC=mycompany,DC=local", "it" },
			{ "CN=ES-Admins,CN=Users,DC=mycompany,DC=local", "$admins" }
		});
	}
}
