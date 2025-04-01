// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.TcpPlugin.Tests;

public class EventStoreOptionsTests {
	static EventStoreOptionsTests() {
		TypeDescriptor.AddAttributes(typeof(IPAddress), new TypeConverterAttribute(typeof(IPAddressConverter)));
	}

	private EventStoreOptions CreateSut(params (string Key, string? Value)[] options) {
		var sut = new ConfigurationBuilder()
			.AddInMemoryCollection(options.Select(
				pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
			.Build()
			.GetSection(KurrentConfigurationKeys.Prefix)
			.Get<EventStoreOptions>() ?? new();

		return sut;
	}

	[Fact]
	public void when_empty_configuration() {
		var sut = CreateSut();
		Assert.False(sut.Insecure);
		Assert.False(sut.TcpPlugin.EnableExternalTcp);
	}

	[Fact]
	public void can_be_enabled() {
		var sut = CreateSut(
			($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp", "true"));
		Assert.True(sut.TcpPlugin.EnableExternalTcp);
	}

	[Fact]
	public void can_be_insecure() {
		var sut = CreateSut(
			($"{KurrentConfigurationKeys.Prefix}:Insecure", "true"));
		Assert.True(sut.Insecure);
	}

	[Fact]
	public void can_set_ip() {
		var sut = CreateSut(
			($"{KurrentConfigurationKeys.Prefix}:NodeIp", "1.2.3.4"));
		Assert.Equal(IPAddress.Parse("1.2.3.4"), sut.NodeIp);
	}
}
