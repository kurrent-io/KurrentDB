// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class Tracer {
	private readonly List<string> _traces = new List<string>();
	private int _depth;

	public Tracer() {
	}

	public string[] ToArray() => _traces.ToArray();

	public void Reset() {
		_traces.Clear();
	}

	public void Trace(string x) {
		_traces.Add(new string(' ', _depth * 4) + x);
	}

	public void TraceIn(string x) {
		Trace(x);
		_depth++;
	}

	public void TraceOut(string x) {
		_depth--;
		Trace(x);
	}

	public static (string, int)  Line(string x, [CallerLineNumber] int sourceLineNumber = 0) =>
		(x, sourceLineNumber);

	// :S
	public static (string, int) AnythingElse { get; } = ("ANYTHING_ELSE", 0);
}
