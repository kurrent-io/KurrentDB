// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Standard;

public abstract class StreamCategoryExtractor {
	private const string ConfigurationFormatIs = "Configuration format is: \r\nfirst|last\r\nseparator";

	public abstract string GetCategoryByStreamId(string streamId);

	public static StreamCategoryExtractor GetExtractor(string source) {
		var trimmedSource = source?.Trim();
		if (string.IsNullOrEmpty(source))
			throw new InvalidOperationException(
				"Cannot initialize categorization projection handler.  "
				+ "One symbol separator or configuration must be supplied in the source.  "
				+ ConfigurationFormatIs);

		if (trimmedSource.Length == 1) {
			var separator = trimmedSource[0];
			var extractor = new StreamCategoryExtractorByLastSeparator(separator);
			return extractor;
		}

		var parts = trimmedSource.Split(['\n']);

		if (parts.Length != 2)
			throw new InvalidOperationException(
				"Cannot initialize categorization projection handler.  "
				+ "Invalid configuration  "
				+ ConfigurationFormatIs);

		var direction = parts[0].ToLowerInvariant().Trim();
		if (direction != "first" && direction != "last")
			throw new InvalidOperationException(
				"Cannot initialize categorization projection handler.  "
				+ "Invalid direction specifier.  Expected 'first' or 'last'. "
				+ ConfigurationFormatIs);

		var separatorLine = parts[1];
		if (separatorLine.Length != 1)
			throw new InvalidOperationException(
				"Cannot initialize categorization projection handler.  "
				+ "Single separator expected. "
				+ ConfigurationFormatIs);

		return direction switch {
			"first" => new StreamCategoryExtractorByFirstSeparator(separatorLine[0]),
			"last" => new StreamCategoryExtractorByLastSeparator(separatorLine[0]),
			_ => throw new Exception($"Unknown direction {direction}")
		};
	}
}
