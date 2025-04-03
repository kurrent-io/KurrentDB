// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using EventStore.Common.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace EventStore.Common.Tests.Configuration;

public class SectionTests {
	[Fact]
	public void SanityCheck() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{"a", "v a"},
				{"sub:a", "v sub:a"},
			})
			.AddSection("section2", b => b
				.AddInMemoryCollection(new Dictionary<string, string?> {
					{"a", "v section2:a"},
					{"b", "v section2:b"},
					{"sub:a", "v section2:sub:a"},
				})
				.AddInMemoryCollection(new Dictionary<string, string?> {
					{"a", "v section2:a2"},
					{"c", "v section2:c"},
					{"sub:b", "v section2:sub:b"},
				}))
			.AddSection("section3", b => b
				.AddInMemoryCollection(new Dictionary<string, string?> {
					{"a", "v section3:a"},
				}))
			.Build();

		Assert.Equal("v a", config["a"]);
		Assert.Equal("v sub:a", config.GetSection("sub")["a"]);

		Assert.Equal("v section2:a2", config.GetSection("section2")["a"]);
		Assert.Equal("v section2:b", config.GetSection("section2")["b"]);
		Assert.Equal("v section2:c", config.GetSection("section2")["c"]);
		
		Assert.Equal("v section2:sub:a", config.GetSection("section2").GetSection("sub")["a"]);
		Assert.Equal("v section2:sub:b", config.GetSection("section2").GetSection("sub")["b"]);

		Assert.Equal("v section3:a", config.GetSection("section3")["a"]);

		// SectionProvider reloads when mounted section changes
		var sectionProvider = config.Providers.OfType<SectionProvider>().First();
		var memConfigProvider = sectionProvider.Providers.OfType<MemoryConfigurationProvider>().First();
		memConfigProvider.Add("new", "v section2:new");
		typeof(ConfigurationProvider).GetMethod("OnReload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(memConfigProvider, null);

		Assert.Equal("v section2:new", config.GetSection("section2")["new"]);

		// can get the provider for a key
		Assert.True(sectionProvider.TryGet("section2:a", out var value));
		Assert.Equal("v section2:a2", value);
		Assert.True(sectionProvider.TryGetProviderFor("section2:a", out var provider));
		Assert.IsType<MemoryConfigurationProvider>(provider);
	}
}
