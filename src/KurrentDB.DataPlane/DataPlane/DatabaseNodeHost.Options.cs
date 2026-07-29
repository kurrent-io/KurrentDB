// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.DataPlane;

using KontrolPlane;

partial class DatabaseNodeHost {
	public class Options {
		private static readonly TimeSpan DefaultPollingPeriod = TimeSpan.FromSeconds(1);

		public TimeSpan PollingPeriod {
			get;
			init => field = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
		} = DefaultPollingPeriod;

		public required DatabaseNode CurrentNode {
			get;
			init;
		}

		public double RenewalRate {
			get;
			init => field = !double.IsNaN(value) && value is > 0D and < 1D ? value : throw new ArgumentOutOfRangeException(nameof(value));
		} = 0.5D;
	}
}
