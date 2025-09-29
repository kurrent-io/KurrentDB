// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KurrentDB.Projections.Core.Services.Processing.Partitioning;

public class PartitionState {
	private static readonly JsonSerializerSettings JsonSettings = new() { DateParseHandling = DateParseHandling.None };

	public bool IsChanged(PartitionState newState) => State != newState.State || Result != newState.Result;

	public static PartitionState Deserialize(string serializedState, CheckpointTag causedBy) {
		if (serializedState == null)
			return new("", null, causedBy);

		JToken state = null;
		JToken result = null;

		if (!string.IsNullOrEmpty(serializedState)) {
			var deserialized = JsonConvert.DeserializeObject(serializedState, JsonSettings);
			if (deserialized is JArray { Count: > 0 } array) {
				state = array[0];
				if (array.Count == 2) {
					result = array[1];
				}
			} else {
				state = deserialized as JObject;
			}
		}

		var stateJson = state != null ? state.ToCanonicalJson() : "";
		var resultJson = result?.ToCanonicalJson();

		return new(stateJson, resultJson, causedBy);
	}

	public PartitionState(string state, string result, CheckpointTag causedBy) {
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(causedBy);

		State = state;
		Result = result;
		CausedBy = causedBy;
		Size = State.Length + (Result?.Length ?? 0);
	}

	public string State { get; }
	public CheckpointTag CausedBy { get; }
	public string Result { get; }
	public int Size { get; }

	public string Serialize() {
		if (State == "" && Result != null)
			throw new Exception("state == \"\" && Result != null");
		return Result != null ? $"[{State},{Result}]" : $"[{State}]";
	}
}
