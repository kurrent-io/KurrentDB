// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Metrics;

namespace EventStore.Core.Services.VNode;

public interface IInaugurationStatusTracker {
	void OnStateChange(InaugurationManager.ManagerState newState);
}

public interface INodeStatusTracker : IInaugurationStatusTracker {
	void OnStateChange(VNodeState newState);
}

public class NodeStatusTracker : INodeStatusTracker {
	private readonly StatusSubMetric _subMetric;
	private readonly object _lock = new();

	private VNodeState _nodeState;
	private InaugurationManager.ManagerState _inaugurationState;

	public NodeStatusTracker(StatusMetric metric) {
		_nodeState = VNodeState.Initializing;
		_inaugurationState = InaugurationManager.ManagerState.Idle;
		_subMetric = new StatusSubMetric("Node", _nodeState, metric);
	}

	public void OnStateChange(VNodeState newState) {
		lock (_lock) {
			_nodeState = newState;
			UpdateStatus();
		}
	}

	public void OnStateChange(InaugurationManager.ManagerState newState) {
		lock (_lock) {
			_inaugurationState = newState;
			UpdateStatus();
		}
	}

	private void UpdateStatus() {
		_subMetric.SetStatus(_inaugurationState == InaugurationManager.ManagerState.Idle
			? $"{_nodeState}"
			: $"{_nodeState} - {_inaugurationState}");
	}

	public class NoOp : INodeStatusTracker {
		public void OnStateChange(VNodeState newState) {
		}

		public void OnStateChange(InaugurationManager.ManagerState newState) {
		}
	}
}
