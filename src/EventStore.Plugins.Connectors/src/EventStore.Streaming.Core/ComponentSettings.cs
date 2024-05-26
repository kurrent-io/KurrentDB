// using System.Globalization;
// using System.Text.RegularExpressions;
//
// namespace EventStore.Streaming;
//
// [PublicAPI]
// public class ComponentSettings : Dictionary<string, string?> {
// 	public ComponentSettings() : base(StringComparer.Ordinal) { }
//
// 	public ComponentSettings(IDictionary<string, string?> settings) : base(settings, StringComparer.Ordinal) {
// 		foreach (var (key, value) in settings)
// 			Add(key, value);
// 	}
// 	
// 	public ComponentSettings Add(string key, string? value, bool validate) {
// 		if (validate) {
// 			if (!ComponentSettingsValidator.IsValidKey(key))
// 				throw new($"Invalid settings key [{key}]");
// 		
// 			if (!ComponentSettingsValidator.IsValidValue(key))
// 				throw new($"Invalid settings value [{key}]");
// 		}
// 		
// 		try {
// 			base.Add(key, value);
// 			return this;
// 		}
// 		catch (Exception ex) {
// 			throw new($"Failed to add setting [{key}]", ex);
// 		}
// 	}
// 	
// 	public new ComponentSettings Add(string key, string? value) => Add(key, value, true);
// 	
// 	public ComponentSettings Add(KeyValuePair<string, string?> entry, bool validate = true) => Add(entry.Key, entry.Value, validate);
// 	
// 	public static ComponentSettings Load(IDictionary<string, string?> entries, out ComponentSettings invalidEntries) {
// 		var valid = new ComponentSettings();
// 		invalidEntries = new ComponentSettings();
//
// 		foreach (var entry in entries) {
// 			if (ComponentSettingsValidator.IsValidEntry(entry))
// 				valid.Add(entry);
// 			else
// 				invalidEntries.Add(entry, false);
// 		}
//
// 		return valid;
// 	}
// 	
// 	// public Settings Add<T>(string key, T value) where T : Enum => Add(key, value.ToString());
//
// 	public ComponentSettings Add(string key, bool value)   => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, int value)    => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, long value)   => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, short value)  => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, double value) => Add(key, value.ToString(CultureInfo.InvariantCulture));
//
// 	public ComponentSettings Add(string key, bool? value)   => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, int? value)    => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, long? value)   => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, short? value)  => Add(key, value.ToString());
// 	public ComponentSettings Add(string key, double? value) => Add(key, value?.ToString(CultureInfo.InvariantCulture));
// }
//
// [PublicAPI]
// public partial class ComponentSettingsValidator {
// 	static readonly Regex KeyValidator   = InternalKeyRegEx();
// 	static readonly Regex ValueValidator = InternalValueRegEx();
//
// 	public static bool IsValidKey(ReadOnlySpan<char> key)     => KeyValidator.IsMatch(key);
// 	public static bool IsValidValue(ReadOnlySpan<char> value) => ValueValidator.IsMatch(value);
//
// 	public static bool IsValidEntry(string key, string? value) => 
// 		IsValidKey(key) && (value is null || IsValidValue(value));
// 	
// 	public static bool IsValidEntry(KeyValuePair<string,string?> entry) => 
// 		IsValidEntry(entry.Key, entry.Value);
// 	
// 	public static bool IsValidEntry((string Key, string? Value) entry) => 
// 		IsValidEntry(entry.Key, entry.Value);
//
// 	[GeneratedRegex("^[a-zA-Z]([a-zA-Z0-9_.-]*(?::[a-zA-Z0-9_.-]+)*[a-zA-Z0-9])?$", RegexOptions.Compiled)]
// 	private static partial Regex InternalKeyRegEx();
// 	
// 	[GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_:/\\\\.-]*[a-zA-Z0-9]?$", RegexOptions.Compiled)]
// 	private static partial Regex InternalValueRegEx();
// }
//
// public static class SettingsExtensions {
//     public static string ConsumerName(this ComponentSettings settings)     => settings.GetValue("consumer.name");
//     public static string SubscriptionName(this ComponentSettings settings) => settings.GetValue("subscription.name");
//     public static string ConsumerKey(this ComponentSettings settings)      => settings.GetValue("consumer.key");
//     public static string SubscriptionKey(this ComponentSettings settings)  => settings.GetValue("subscription.key");
//     public static string Streams(this ComponentSettings settings)          => settings.GetValue("streams");
//
//     static string GetValue(this ComponentSettings settings, string key) {
//         settings.TryGetValue(key, out var value);
//         return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
//     }
// }