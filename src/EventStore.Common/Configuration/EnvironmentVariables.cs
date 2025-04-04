using System;
using System.Collections;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Configuration {
	internal class EnvironmentVariables : ConfigurationProvider {
		private readonly IDictionary _environment;
		private readonly string _prefix;

		public EnvironmentVariables(string prefix, IDictionary environment) {
			if (environment == null) {
				throw new ArgumentNullException(nameof(environment));
			}

			_prefix = $"{prefix}_";
			_environment = environment;
		}

		public override void Load() {
			Data.Clear();

			foreach (var k in _environment.Keys) {
				var key = k.ToString() ?? _prefix;
				if (!key.StartsWith(_prefix)) {
					continue;
				}

				Data[StringExtensions.Computerize(key.Remove(0, _prefix.Length))] = _environment[k]?.ToString();
			}
		}
	}
}
