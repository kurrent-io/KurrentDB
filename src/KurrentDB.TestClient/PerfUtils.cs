// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using ILogger = Serilog.ILogger;
#pragma warning disable 1591

namespace KurrentDB.TestClient;

public static class PerfUtils {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(PerfUtils));

	private const string ColumnSplitter = ";";
	private const string PairSplitter = ":";

	public static NameValue Col(string name, object value) {
		return new NameValue(name, value);
	}

	public static NameValue[] Row(params NameValue[] nameValuesList) {
		return nameValuesList;
	}

	public static void LogData(string dataName, params NameValue[][] rows) {
		var sb = new StringBuilder();

		sb.AppendLine("[[begin");
		sb.AppendLine(Format("DataName", dataName));
		foreach (var cols in rows) {
			foreach (var nameValue in cols) {
				sb.Append(Format(nameValue.Name, nameValue.Value));
			}

			sb.AppendLine();
		}

		sb.AppendLine("end]]");

		Log.Debug(sb.ToString());
	}

	private static string Format(string name, object value) {
		return string.Format("{0}{1}{2}{3}", name, PairSplitter, value, ColumnSplitter);
	}

	/// <summary>
	/// Prints key-value point to the log in a way that TeamCity build server
	/// would be able to capture and then plot on build statistics page,
	/// tracking performance across multiple builds (need server-side config per project).
	/// </summary>
	public static void LogTeamCityGraphData(string key, long value) {
		if (value < 0) {
			Log.Error("Value is {value}, however TeamCity requires Value as a positive (non negative) integer.",
				value);
			return;
		}

		Log.Debug("\n##teamcity[buildStatisticValue key='{key}' value='{value}']", key, value);
	}

	public class NameValue {
		public readonly string Name;
		public readonly object Value;

		public NameValue(string name, object value) {
			Name = name;
			Value = value;
		}
	}
}
