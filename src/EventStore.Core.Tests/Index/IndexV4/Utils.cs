// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Numerics;

namespace EventStore.Core.Tests.Index.IndexV4;

public static class Utils {
	public static bool IsMidpointIndex(long index, long numIndexEntries, int numMidpoints) {
		//special cases
		if (numIndexEntries < 1) return false;
		if (numIndexEntries == 1) {
			if (numMidpoints == 2 && index == 0) return true;
			return false;
		}

		//a midpoint index entry satisfies:
		//index = floor (k * (numIndexEntries - 1) / (numMidpoints - 1));    for k = 0 to numMidpoints-1
		//we need to find if there exists an integer x, such that:
		//index*(numMidpoints-1)/(numIndexEntries-1) <= x < (index+1)*(numMidpoints-1)/(numIndexEntries-1)
		var lower = (BigInteger) index * (numMidpoints - 1) / (numIndexEntries - 1);
		if (((BigInteger) index * (numMidpoints - 1)) % (numIndexEntries - 1) != 0) lower++;
		var upper = (BigInteger) (index + 1) * (numMidpoints - 1) / (numIndexEntries - 1);
		if (((BigInteger) (index + 1) * (numMidpoints - 1)) % (numIndexEntries - 1) == 0) upper--;
		return lower <= upper;
	}
}
