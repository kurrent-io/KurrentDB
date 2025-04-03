// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace EventStore.Core;

public record SectionMetadata(
	string SectionName,
	string Description,
	Type SectionType,
	Dictionary<string, OptionMetadata> Options,
	int Sequence) {

	public static SectionMetadata FromPropertyInfo(PropertyInfo property, int sequence) {
		var description = property.PropertyType.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

		var metadata = new SectionMetadata(
			SectionName: property.Name,
			Description: description,
			SectionType: property.PropertyType,
			Options: [],
			Sequence: sequence
		);

		var optionProps = property.PropertyType.GetProperties();
		for (var i = 0; i < optionProps.Length; i++) {
			var optionMetadata = OptionMetadata.FromPropertyInfo(metadata, optionProps[i], i);
			metadata.Options[optionMetadata.Key] = optionMetadata;
		}
	
		var options = property.PropertyType.GetProperties()
			.Select((p, i) => OptionMetadata.FromPropertyInfo(metadata, p, i))
			.ToDictionary(option => option.Key, x => x);

		return metadata;
	}
}
