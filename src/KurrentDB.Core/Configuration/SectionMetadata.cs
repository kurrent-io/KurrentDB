// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace KurrentDB.Core;

public record SectionMetadata(
	string SectionName,
	string Description,
	Type SectionType,
	Dictionary<string, OptionMetadata> Options,
	int Sequence) {

	public static SectionMetadata FromPropertyInfo(PropertyInfo property, int sequence) {
		var sectionType = GetSectionType(property);
		var description = sectionType.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

		var metadata = new SectionMetadata(
			SectionName: property.Name,
			Description: description,
			SectionType: property.PropertyType,
			Options: [],
			Sequence: sequence
		);

		var optionProps = sectionType.GetProperties();
		for (var i = 0; i < optionProps.Length; i++) {
			var optionMetadata = OptionMetadata.FromPropertyInfo(metadata, optionProps[i], i);
			metadata.Options[optionMetadata.Key] = optionMetadata;
		}

		var options = sectionType.GetProperties()
			.Select((p, i) => OptionMetadata.FromPropertyInfo(metadata, p, i))
			.ToDictionary(option => option.Key, x => x);

		return metadata;
	}

	// Every option group type implements IConfigurationBinder<TSelf>, whose TSelf annotation
	// preserves the public properties and the parameterless constructor of the section type.
	// Kept here rather than in ClusterVNodeOptions to avoid triggering its static constructor.
	[UnconditionalSuppressMessage("Trimming", "IL2073",
		Justification = "Option group types implement IConfigurationBinder<TSelf>, which preserves the required members")]
	[return: DynamicallyAccessedMembers(
		DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
	internal static Type GetSectionType(PropertyInfo optionGroup) => optionGroup.PropertyType;
}
