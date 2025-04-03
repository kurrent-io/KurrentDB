// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public class PartitionState {
	private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
		DateParseHandling = DateParseHandling.None,
	};

	public bool IsChanged(PartitionState newState) {
		return State != newState.State || Result != newState.Result;
	}

	public static PartitionState Deserialize(string serializedState, CheckpointTag causedBy) {
		if (serializedState == null)
			return new PartitionState("", null, causedBy);

		JToken state = null;
		JToken result = null;

		if (!string.IsNullOrEmpty(serializedState)) {
			var deserialized = JsonConvert.DeserializeObject(serializedState, JsonSettings);
			var array = deserialized as JArray;
			if (array != null && array.Count > 0) {
				state = array[0] as JToken;
				if (array.Count == 2) {
					result = array[1] as JToken;
				}
			} else {
				state = deserialized as JObject;
			}
		}

		var stateJson = state != null ? state.ToCanonicalJson() : "";
		var resultJson = result != null ? result.ToCanonicalJson() : null;

		return new PartitionState(stateJson, resultJson, causedBy);
	}

	private static void Error(JsonTextReader reader, string message) {
		throw new Exception(string.Format("{0} (At: {1}, {2})", message, reader.LineNumber, reader.LinePosition));
	}

	private readonly string _state;
	private readonly string _result;
	private readonly CheckpointTag _causedBy;
	private readonly int _size;

	public PartitionState(string state, string result, CheckpointTag causedBy) {
		if (state == null) throw new ArgumentNullException("state");
		if (causedBy == null) throw new ArgumentNullException("causedBy");

		_state = state;
		_result = result;
		_causedBy = causedBy;
		_size = _state.Length + (_result?.Length ?? 0);
	}

	public string State {
		get { return _state; }
	}

	public CheckpointTag CausedBy {
		get { return _causedBy; }
	}

	public string Result {
		get { return _result; }
	}

	public int Size {
		get { return _size; }
	}

	public string Serialize() {
		var state = _state;
		if (state == "" && Result != null)
			throw new Exception("state == \"\" && Result != null");
		return Result != null
			? "[" + state + "," + _result + "]"
			: "[" + state + "]";
	}
}
