// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

namespace EventStore.Core;

public record LoadedOption {
	public LoadedOption(
		OptionMetadata metadata,
		string title,
		string? value,
		string sourceDisplayName,
		bool isDefault) {
		RawValue = value;
		Metadata = metadata;
		Title = title;
		DisplayValue = metadata.IsSensitive ? new('*', 8) : value ?? string.Empty;
		SourceDisplayName = sourceDisplayName;
		IsDefault = isDefault;
	}

	string? RawValue { get; }

	public OptionMetadata Metadata { get; }
	public string Title { get; }
	public string DisplayValue { get; }
	public string SourceDisplayName { get; }
	public bool IsDefault { get; }

	/// <summary>
	/// Returns the raw value of the option. Be aware that this value may be sensitive.
	/// </summary>
	public string? GetRawValue() => RawValue;

	public override string ToString() => DisplayValue;
}
