// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace KurrentDB.Common.Configuration;

public sealed class SectionProvider : ConfigurationProvider, IDisposable {
	private readonly IConfigurationRoot _configuration;
	private readonly string _sectionName;
	private readonly IDisposable _registration;

	public SectionProvider(string sectionName, IConfigurationRoot configuration) {
		_configuration = configuration;
		_sectionName = sectionName;
		_registration = ChangeToken.OnChange(
			configuration.GetReloadToken,
			Load);
	}

	public IEnumerable<IConfigurationProvider> Providers => _configuration.Providers;

	public bool TryGetProviderFor(string key, out IConfigurationProvider provider) {
		var prefix = _sectionName + ":";
		if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
			key = key[prefix.Length..];
			foreach (var candidate in Providers) {
				if (candidate.TryGet(key, out _)) {
					provider = candidate;
					return true;
				}
			}
		}

		provider = default;
		return false;
	}

	public void Dispose() {
		_registration.Dispose();
	}

	public override void Load() {
		var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var kvp in _configuration.AsEnumerable()) {
			// the enumerable contains sections even if they are null,
			//
			// key           value
			// GossipSeed    null
			// GossipSeed:0  "host1:2113"
			// GossipSeed:1  "host2:2113"
			//
			// since net10 nulls count as values and are not ignored, and so we only want to add
			// the null to the data if one of the providers is truly providing it and it isn't just
			// a section declaration.
			if (kvp.Value is null && !_configuration.Providers.Any(p => p.TryGet(kvp.Key, out _)))
				continue;

			data[_sectionName + ":" + kvp.Key] = kvp.Value;
		}
		Data = data;
		OnReload();
	}
}
