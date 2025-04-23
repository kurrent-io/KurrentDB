using Microsoft.Extensions.Configuration;

namespace KurrentDB.Toolkit.Testing.Extensions;

public static class ConfigurationExtensions {
	public static IConfiguration EnsureValue(this IConfiguration configuration, string key, string defaultValue) {
		var value = configuration.GetValue<string?>(key);

		if (string.IsNullOrEmpty(value))
			configuration[key] = defaultValue;

		return configuration;
	}
}