using Microsoft.Extensions.Configuration;

namespace KurrentDB.Testing;

public static class ConfigurationExtensions {
    public static T GetOptionsOrDefault<T>(this IConfiguration configuration, string sectionName) where T : new() {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        var options = configuration.GetSection(sectionName).Get<T>(options => options.BindNonPublicProperties = true);
        return options ?? new T();
    }

    public static T GetOptionsOrDefault<T>(this IConfiguration configuration) where T : new() =>
        configuration.Get<T>(options => options.BindNonPublicProperties = true) ?? new T();

    public static T GetRequiredOptions<T>(this IConfiguration configuration) {
        var options = configuration.Get<T>(options => options.BindNonPublicProperties = true);
        return options ?? throw new InvalidOperationException($"Failed to load {typeof(T).Name}");
    }

    public static T GetRequiredOptions<T>(this IConfiguration configuration, string sectionName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        var options = configuration.GetRequiredSection(sectionName).Get<T>(options => options.BindNonPublicProperties = true);
        return options ?? throw new InvalidOperationException($"Failed to load {typeof(T).Name}");
    }

	public static string[] Values(this IConfiguration configuration, string key, char separator = ',') {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

		var value = configuration.GetValue(key, string.Empty);

        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Contains(separator)
                ? value.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [value];
	}

	public static T Value<T>(this IConfiguration configuration, string key, T defaultValue) {
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		return configuration.GetValue(key, defaultValue)!;
	}

	public static T GetFirstValue<T>(this IConfiguration configuration, string[] keys, T defaultValue) {
        ArgumentOutOfRangeException.ThrowIfZero(keys.Length);
        ArgumentNullException.ThrowIfNull(defaultValue);

		T? value = default;
		while (value is null && keys.Length > 0) {
			value = configuration.GetValue<T?>(keys[0], default);
			keys  = keys[1..];
		}

		return value ?? defaultValue;
	}
}

public static class ConfigurationSettingsExtensions {
    public static IConfiguration ToConfiguration(this IDictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    public static IDictionary<string, string?> ToSettings(this IConfiguration configuration) =>
        new Dictionary<string, string?>(configuration.AsEnumerable().Where(x => x.Value is not null));
}
