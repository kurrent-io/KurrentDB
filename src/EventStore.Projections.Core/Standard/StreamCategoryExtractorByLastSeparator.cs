// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Standard;

public class StreamCategoryExtractorByLastSeparator : StreamCategoryExtractor {
	private readonly char _separator;

	public StreamCategoryExtractorByLastSeparator(char separator) {
		_separator = separator;
	}

	public override string GetCategoryByStreamId(string streamId) {
		string category = null;
		if (!streamId.StartsWith("$")) {
			var lastSeparatorPosition = streamId.LastIndexOf(_separator);
			if (lastSeparatorPosition > 0)
				category = streamId.Substring(0, lastSeparatorPosition);
		}

		return category;
	}
}
