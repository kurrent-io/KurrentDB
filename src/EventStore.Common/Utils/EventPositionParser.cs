// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Common.Utils;

public abstract class EventPositionParser {
	public static (long commit, long prepare) ParseCommitPreparePosition(string position) {
		if (string.IsNullOrEmpty(position))
			return (-1, -1);

		string[] parts = position.Split('/');
		if (parts.Length != 2 || parts[0][0] != 'C' || parts[1][0] != 'P')
			throw new Exception($"Invalid event position: {position}");

		if (!long.TryParse(parts[0].Substring(2), out long commit))
			throw new Exception($"Invalid commit position in event position: {position}");

		if (!long.TryParse(parts[1].Substring(2), out long prepare))
			throw new Exception($"Invalid prepare position in event position: {position}");

		return (commit, prepare);
	}
}
