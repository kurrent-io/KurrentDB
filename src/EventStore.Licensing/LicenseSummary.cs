// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using EventStore.Plugins.Licensing;

namespace EventStore.Licensing;

public record LicenseSummary(
	string LicenseId,
	string Company,
	bool IsTrial,
	long ExpiryUnixTimeSeconds,
	bool IsValid,
	string Notes) {

	static readonly string IsExpiredName = "isExpired";
	static readonly string ExpiryUnixTimeSecondsName = ToCamelCase(nameof(ExpiryUnixTimeSeconds));

	public LicenseSummary(string licenseId, string company, bool isTrial, DateTimeOffset expiry, bool isValid, string notes)
		: this(licenseId, company, isTrial, expiry.ToUnixTimeSeconds(), isValid, notes) {
	}

	public void ExportClaims(in Dictionary<string, object> props) {
		foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
			props.Add(ToCamelCase(property.Name), property.GetValue(this)!);
	}

	public static HashSet<string> Properties { get; } =
		typeof(LicenseSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(p => ToCamelCase(p.Name))
			.ToHashSet();

	public static Dictionary<string, object?> SelectForEndpoint(License license) {
		var dict = new Dictionary<string, object?>();

		dict[IsExpiredName] = "false";

		foreach (var claim in license.Token.Claims ?? []) {
			if (claim.Type == ExpiryUnixTimeSecondsName) {
				var daysRemaining = CalcDaysRemaining(claim.Value);

				dict["daysRemaining"] = $"{daysRemaining:N2}";
				if (daysRemaining <= 0)
					dict[IsExpiredName] = "true";

			} else if (Properties.Contains(claim.Type)) {
				dict[claim.Type] = claim.Value;
			}
		}

		// needed for backwards compatibility with the webui
		dict["isFloating"] = "true";
		dict["startDate"] = "0";
		return dict;
	}

	static double CalcDaysRemaining(string expiryUnixTimeSecondsString) {
		var expiryUnixTimeSeconds = long.Parse(expiryUnixTimeSecondsString);
		var expiry = DateTimeOffset.FromUnixTimeSeconds(expiryUnixTimeSeconds);
		var daysRemaining = (expiry - DateTimeOffset.UtcNow).TotalDays;
		daysRemaining = Math.Max(daysRemaining, 0);
		return daysRemaining;
	}

	public static Dictionary<string, object?> SelectForTelemetry(License license) {
		var dict = new Dictionary<string, object?>();

		AddString(nameof(LicenseId), license, dict);
		AddBool(nameof(IsTrial), license, dict);

		if (TryGet(ExpiryUnixTimeSecondsName, license, out var _, out var value)) {
			dict[IsExpiredName] = CalcDaysRemaining(value) <= 0;
		}

		AddBool(nameof(IsValid), license, dict);

		static void AddString(string property, License license, Dictionary<string, object?> dict) {
			if (TryGet(property, license, out var k, out var v))
				dict[k] = v;
		}

		static void AddBool(string property, License license, Dictionary<string, object?> dict) {
			if (TryGet(property, license, out var k, out var v) && bool.TryParse(v, out var b))
				dict[k] = b;
		}

		static bool TryGet(string property, License l, out string key, [MaybeNullWhen(false)] out string value) {
			key = ToCamelCase(property);
			foreach (var claim in l.Token.Claims ?? []) {
				if (key == claim.Type) {
					value = claim.Value;
					return true;
				}
			}
			value = default;
			return false;
		};

		return dict;
	}

	private static string ToCamelCase(string pascalCaseStr) {
		if (string.IsNullOrEmpty(pascalCaseStr))
			return pascalCaseStr;

		return char.ToLower(pascalCaseStr[0]) + pascalCaseStr[1..];
	}
}
