// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using MudBlazor;

namespace KurrentDB.Tools;

public class ChartSeries : List<TimeSeriesChartSeries> {
	readonly DateTime _now = DateTime.Now;

	public ChartSeriesValues Add(string name) {
		var series = new ChartSeriesValues(name, _now);
		Add(series);
		return series;
	}
}

public class ChartSeriesValues : TimeSeriesChartSeries {
	public ChartSeriesValues(string name, DateTime now) {
		var empty = Enumerable.Range(-120, 120).Select(x => new TimeValue(now.AddSeconds(x * 10), 0)).ToList();
		Index = 0;
		Name = name;
		Data = empty;
		IsVisible = true;
		Type = TimeSeriesDisplayType.Area;
	}

	public void AddData(double value) {
		Data = [..Data.Skip(1), new(DateTime.Now, value)];
	}
}
