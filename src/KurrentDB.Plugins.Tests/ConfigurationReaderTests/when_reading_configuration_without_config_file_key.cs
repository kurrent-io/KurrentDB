// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Plugins.Tests.ConfigurationReaderTests;

public class when_reading_configuration_without_config_file_key {
	[Fact]
	public void should_read_settings_from_main_configuration() {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:LdapsAuth:Host", "192.168.1.1" },
				{ "KurrentDB:LdapsAuth:Port", "636" },
				{ "KurrentDB:LdapsAuth:BindUser", "domain\\reader" },
				{ "KurrentDB:LdapsAuth:BindPassword", "secret" },
				{ "KurrentDB:LdapsAuth:BaseDn", "dc=example,dc=com" },
			})
			.Build();

		var settings = new ConfigParser(NullLogger.Instance)
			.ReadConfiguration<LdapsSettings>(configuration, "AuthenticationConfig", "LdapsAuth");

		settings.Host.Should().Be("192.168.1.1");
		settings.Port.Should().Be(636);
		settings.BindUser.Should().Be("domain\\reader");
		settings.BindPassword.Should().Be("secret");
		settings.BaseDn.Should().Be("dc=example,dc=com");
	}

	[Fact]
	public void should_throw_when_section_is_absent() {
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection([])
			.Build();

		var act = () => new ConfigParser(NullLogger.Instance)
			.ReadConfiguration<LdapsSettings>(configuration, "AuthenticationConfig", "LdapsAuth");

		act.Should().Throw<Exception>()
			.WithMessage("Could not read LdapsAuth configuration from main configuration");
	}
}
