// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Plugins.Tests.ConfigurationReaderTests;

public class when_reading_configuration_with_config_file_key {
	[Fact]
	public void should_read_settings_from_yaml_file() {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:AuthenticationConfig", Path.Combine("ConfigurationReaderTests", "valid_node_config.yaml") }
			})
			.Build();

		var settings = new ConfigParser(NullLogger.Instance)
			.ReadConfiguration<LdapsSettings>(configuration, "AuthenticationConfig", "LdapsAuth");

		settings.Host.Should().Be("13.64.104.29");
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
	}

	[Fact]
	public void should_throw_when_file_does_not_exist() {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:AuthenticationConfig", "nonexistent.yaml" }
			})
			.Build();

		var act = () => new ConfigParser(NullLogger.Instance)
			.ReadConfiguration<LdapsSettings>(configuration, "AuthenticationConfig", "LdapsAuth");

		act.Should().Throw<FileNotFoundException>();
	}
}
