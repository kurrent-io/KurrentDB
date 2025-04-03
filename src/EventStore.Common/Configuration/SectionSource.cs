// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Configuration;

public class SectionSource : IConfigurationSource {
	private readonly string _sectionName;
	private readonly Action<IConfigurationBuilder> _configure;

	public SectionSource(string sectionName, Action<IConfigurationBuilder> configure) {
		_sectionName = sectionName;
		_configure = configure;
	}

	public IConfigurationProvider Build(IConfigurationBuilder builder) {
		var subBuilder = new ConfigurationBuilder();
		_configure(subBuilder);
		var configuration = subBuilder.Build();

		return new SectionProvider(_sectionName, configuration);
	}
}

public static class SectionConfigurationExtensions {
	// Allows configuration to be mounted inside a specified section
	public static IConfigurationBuilder AddSection(this IConfigurationBuilder self, string sectionName,
		Action<IConfigurationBuilder> configure) =>
		self.Add(new SectionSource(sectionName, configure));
}
