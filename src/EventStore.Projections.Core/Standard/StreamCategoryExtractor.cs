// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Projections.Core.Standard;

public abstract class StreamCategoryExtractor {
	private const string ConfigurationFormatIs = "Configuration format is: \r\nfirst|last\r\nseparator";

	public abstract string GetCategoryByStreamId(string streamId);

	public static StreamCategoryExtractor GetExtractor(string source, Action<string, object[]> logger) {
		var trimmedSource = source == null ? null : source.Trim();
		if (string.IsNullOrEmpty(source))
			throw new InvalidOperationException(
				"Cannot initialize categorization projection handler.  "
				+ "One symbol separator or configuration must be supplied in the source.  "
				+ ConfigurationFormatIs);

		if (trimmedSource.Length == 1) {
			var separator = trimmedSource[0];
			if (logger != null) {
/*
                    logger(
                        String.Format(
                            "Categorize stream projection handler has been initialized with separator: '{0}'", separator));
*/
			}

			var extractor = new StreamCategoryExtractorByLastSeparator(separator);
			return extractor;
		}

		var parts = trimmedSource.Split(new[] {'\n'});

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

		switch (direction) {
			case "first":
				return new StreamCategoryExtractorByFirstSeparator(separatorLine[0]);
			case "last":
				return new StreamCategoryExtractorByLastSeparator(separatorLine[0]);
			default:
				throw new Exception();
		}
	}
}
