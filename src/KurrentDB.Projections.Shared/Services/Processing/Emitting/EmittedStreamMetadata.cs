// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class EmittedStreamMetadata {
	private readonly int? _maxCount;
	private readonly TimeSpan? _maxAge;

	public EmittedStreamMetadata(int? maxCount = null, TimeSpan? maxAge = null) {
		_maxCount = maxCount;
		_maxAge = maxAge;
	}

	public int? MaxCount {
		get { return _maxCount; }
	}

	public TimeSpan? MaxAge {
		get { return _maxAge; }
	}
}
