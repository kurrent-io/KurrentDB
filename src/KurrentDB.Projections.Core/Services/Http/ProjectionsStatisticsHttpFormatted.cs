// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Http;

public class ProjectionsStatisticsHttpFormatted {
	public ProjectionsStatisticsHttpFormatted(
		ProjectionManagementMessage.Statistics source, Func<string, string> makeAbsoluteUrl) {
		Projections = source.Projections.Select(v => new ProjectionStatisticsHttpFormatted(v, makeAbsoluteUrl)).ToArray();
	}

	public ProjectionStatisticsHttpFormatted[] Projections { get; }
}
