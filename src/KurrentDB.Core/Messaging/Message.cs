// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;

namespace KurrentDB.Core.Messaging;

[AttributeUsage(AttributeTargets.Class)]
public class BaseMessageAttribute : Attribute {
	public BaseMessageAttribute() {
	}
}

[AttributeUsage(AttributeTargets.Class)]
public class DerivedMessageAttribute : Attribute {
	public DerivedMessageAttribute() {
	}

	public DerivedMessageAttribute(object messageGroup) {
	}
}

[BaseMessage]
public abstract partial class Message(CancellationToken token = default) {
	[JsonIgnore]
	public CancellationToken CancellationToken => token;

	[JsonIgnore]
	public bool Trace { get; init; }

	public string[] GetTraceMessages() {
		var list = new List<string>();
		foreach (var message in GetTrace()) {
			if (message is string m) {
				list.Add(m);
			} else if (message is Message msg) {
				var msgName = msg.GetType().Name;
				foreach (var y in msg.GetTraceMessages()) {
					list.Add($"  {msgName}: {y}");
				}
			}
		}
		return list.ToArray();
	}

	public object[] GetTrace() {
		lock (this) {
			return _traceMessages.ToArray();
		}
	}

	private List<object> _traceMessages;

	public void AddTrace(object message) {
		if (!Trace)
			return;

		lock (this) {
			_traceMessages ??= [];
			if (message is string s) {
				message = $"[{DateTime.UtcNow}] {s}";
			}
			_traceMessages.Add(message);
		}
	}
}
