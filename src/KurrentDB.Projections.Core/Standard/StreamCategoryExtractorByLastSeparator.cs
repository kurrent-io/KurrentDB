// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Standard;

public class StreamCategoryExtractorByLastSeparator(char separator) : StreamCategoryExtractor {
	public override string GetCategoryByStreamId(string streamId) {
		string category = null;
		if (!streamId.StartsWith('$')) {
			var lastSeparatorPosition = streamId.LastIndexOf(separator);
			if (lastSeparatorPosition > 0)
				category = streamId[..lastSeparatorPosition];
		}

		return category;
	}
}
