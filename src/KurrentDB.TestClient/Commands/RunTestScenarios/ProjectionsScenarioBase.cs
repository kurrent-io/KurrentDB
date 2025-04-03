// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using EventStore.Transport.Http.Codecs;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal abstract class ProjectionsScenarioBase : ScenarioBase {
	protected ProjectionsScenarioBase(Action<IPEndPoint, byte[]> directSendOverTcp, int maxConcurrentRequests,
		int connections, int streams, int eventsPerStream, int streamDeleteStep, string dbParentPath,
		NodeConnectionInfo customNode)
		: base(directSendOverTcp, maxConcurrentRequests, connections, streams, eventsPerStream, streamDeleteStep,
			dbParentPath, customNode) {
	}

	protected bool CheckProjectionState(string projectionName, string key, Func<string, bool> checkValue) {
		var state = GetProjectionState(projectionName);
		string value;
		return state != null && state.Count > 0 && state.TryGetValue(key, out value) && checkValue(value);
	}

	protected T GetProjectionStateValue<T>(string projectionName, string key, Func<string, T> convert,
		T defaultValue) {
		var result = defaultValue;

		var state = GetProjectionState(projectionName);
		string value;
		if (state != null && state.Count > 0 && state.TryGetValue(key, out value))
			result = convert(value);

		return result;
	}

	protected bool GetProjectionIsRunning(string projectionName) {
		var dic = GetProjectionStatistics(projectionName);

		string value;
		var isRunning = dic != null && dic.TryGetValue("status", out value) && value == "Running";

		return isRunning;
	}

	protected long GetProjectionPosition(string projectionName) {
		var dic = GetProjectionStatistics(projectionName);

		long result = -1;

		string value;
		if (dic != null && dic.TryGetValue("position", out value)) {
			if (!string.IsNullOrWhiteSpace(value)) {
				var subpositions = value.Split(':');
				var positionString = subpositions.Length == 2 ? subpositions[1] : value;
				result = long.Parse(positionString);
			} else {
				result = -1;
			}

			return result;
		}

		return result;
	}

	protected bool GetProjectionIsFaulted(string projectionName, out string reason) {
		var dic = GetProjectionStatistics(projectionName);

		string status;
		var isFaulted = dic != null && dic.TryGetValue("status", out status) && status.StartsWith("Faulted");

		if (isFaulted)
			dic.TryGetValue("stateReason", out reason);
		else
			reason = null;

		return isFaulted;
	}

	private Dictionary<string, string> GetProjectionStatistics(string projectionName) {
		string rawState;
		try {
			rawState = GetProjectionsManager().GetStatisticsAsync(projectionName, AdminCredentials).Result;
		} catch (Exception ex) {
			Log.Information(ex, "Failed to read projection statistics. Will continue.");
			rawState = null;
		}

		Log.Information("Raw {projection} stats: {rawState}", projectionName, rawState);

		if (string.IsNullOrEmpty(rawState))
			return null;

		if (rawState == "*** UNKNOWN ***")
			return null;

		var start = rawState.IndexOf('[');
		var end = rawState.IndexOf(']');

		if (start == -1 || end == -1)
			return null;

		var statDic = rawState.Substring(start + 1, end - start - 1);

		var state = Codec.Json.From<Dictionary<string, string>>(statDic);
		return state;
	}

	private Dictionary<string, string> GetProjectionState(string projectionName) {
		var rawState = GetProjectionStateSafe(projectionName);

		Log.Information("Raw {projection} state: {rawState}", projectionName, rawState);

		if (string.IsNullOrEmpty(rawState))
			return null;

		if (rawState == "*** UNKNOWN ***")
			return null;

		var state = Codec.Json.From<Dictionary<string, string>>(rawState);
		return state;
	}

	private string GetProjectionStateSafe(string projectionName) {
		string rawState;
		try {
			rawState = GetProjectionsManager().GetStateAsync(projectionName, AdminCredentials).Result;
		} catch (Exception ex) {
			rawState = null;
			Log.Information(ex, "Failed to get projection state");
		}

		return rawState;
	}

	protected void EnableProjectionByCategory() {
		const string byCategoryProjection = "$by_category";

		var retryCount = 0;
		Exception exception = null;
		while (retryCount < 5) {
			Thread.Sleep(250 * (retryCount * retryCount));
			try {
				var isRunning = GetProjectionIsRunning(byCategoryProjection);

				if (!isRunning) {
					Log.Debug("Enable *{projection}* projection", byCategoryProjection);
					GetProjectionsManager().EnableAsync(byCategoryProjection, AdminCredentials).Wait();
				} else {
					Log.Debug("Already enabled *{projection}* projection", byCategoryProjection);
				}

				exception = null;
				break;
			} catch (Exception ex) {
				exception = new ApplicationException("Failed to enable by_category.", ex);
				Log.Error(ex, "Failed to enable *$by_category* projection, retry #{retryCount}.",
					retryCount);
			}

			retryCount += 1;
		}

		if (exception != null)
			throw exception;
	}
}
