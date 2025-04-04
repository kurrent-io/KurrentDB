// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog;

namespace EventStore.AutoScavenge;

public static class TaskExtensions {
	public static Task WarnOnCompletion(this Task task, string warning) {
		return task.ContinueWith(t => {
			if (t.Exception is null)
				return;
			Log.Warning(t.Exception, warning);
		});
	}
}
