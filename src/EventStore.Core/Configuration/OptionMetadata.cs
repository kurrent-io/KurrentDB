// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System.ComponentModel;
using System.Net;
using System.Reflection;
using EventStore.Common.Configuration;
using EventStore.Core.Configuration.Sources;
using Newtonsoft.Json.Linq;

namespace EventStore.Core;

public record OptionMetadata(
	string Key,
	string FullKey,
	string Name,
	string Description,
	string[] AllowedValues,
	bool IsSensitive,
	bool IsEnvironmentOnly,
	string? ErrorMessage,
	SectionMetadata SectionMetadata,
	int Sequence,
	JObject OptionSchema,
	string? DeprecationMessage) {
	public static OptionMetadata
		FromPropertyInfo(SectionMetadata sectionMetadata, PropertyInfo property, int sequence) {
		var sectionName = property.DeclaringType?.Name.Replace("Options", "") ?? "";
		var key = KurrentConfigurationKeys.Normalize(property.Name);
		var fullKey =
			$"{KurrentConfigurationKeys.Prefix}:{sectionName}:{KurrentConfigurationKeys.StripConfigurationPrefix(property.Name)}";

		var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
		var isSensitive = property.GetCustomAttribute<SensitiveAttribute>() != null;

		var envOnlyAttribute = property.GetCustomAttribute<EnvironmentOnlyAttribute>();
		var isEnvironmentOnly = envOnlyAttribute != null;
		var errorMessage = envOnlyAttribute?.Message;
		var allowedValues = property.PropertyType.IsEnum ? property.PropertyType.GetEnumNames() : [];
		var unitAttributeMessage = property.GetCustomAttribute<UnitAttribute>()?.Unit;
		var deprecationMessage = property.GetCustomAttribute<DeprecatedAttribute>()?.Message;
		var schema = GetOptionSchema(property, unitAttributeMessage);

		return new(
			Key: key,
			FullKey: fullKey,
			Name: property.Name,
			Description: description,
			AllowedValues: allowedValues,
			IsSensitive: isSensitive,
			IsEnvironmentOnly: isEnvironmentOnly,
			ErrorMessage: errorMessage,
			SectionMetadata: sectionMetadata,
			Sequence: sequence,
			OptionSchema: schema,
			DeprecationMessage: deprecationMessage
		);

	}

	private static JObject GetOptionSchema(PropertyInfo property, string? unitAttributeMessage) {
		if (property.PropertyType.IsEnum) {
			JArray enumValues = new JArray();
			foreach (var item in property.PropertyType.GetEnumNames()) {
				enumValues.Add(item);
			}

			return new JObject {
				["type"] = "string",
				["name"] = property.Name,
				["enum"] = enumValues
			};
		}

		if (property.PropertyType == typeof(bool)) {
			return new JObject {
				["type"] = "boolean"
			};
		}

		if (property.PropertyType == typeof(int) || property.PropertyType == typeof(long) ||
		           property.PropertyType == typeof(double)) {
			if (unitAttributeMessage != null) {
				return new JObject {
					["type"] = "integer",
					["x-unit"] = unitAttributeMessage
				};
			}

			return new JObject {
				["type"] = "integer"
			};
		}

		if (property.PropertyType == typeof(string) || property.PropertyType == typeof(IPAddress)) {
			return new JObject {
				["type"] = "string"
			};
		}

		if (property.PropertyType == typeof(EndPoint[])) {
			return new JObject {
				["type"] = "array",
				["items"] = new JObject {
					["type"] = "string"
				}
			};
		}

		return new JObject();
	}
}
