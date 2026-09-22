// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

partial class DatabaseManager {
	public class Options {
		public double RenewalRate {
			get;
			init => field = !double.IsNaN(value) && value is > 0D and < 1D ? value : throw new ArgumentOutOfRangeException(nameof(value));
		} = 0.5D;
	}
}
