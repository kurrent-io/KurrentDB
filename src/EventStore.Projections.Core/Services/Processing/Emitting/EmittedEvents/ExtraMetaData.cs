// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public class ExtraMetaData {
	private readonly Dictionary<string, string> _metadata;

	public ExtraMetaData(Dictionary<string, JRaw> metadata) {
		_metadata = metadata.ToDictionary(v => v.Key, v => v.Value.ToString());
	}

	public ExtraMetaData(Dictionary<string, string> metadata) {
		_metadata = metadata.ToDictionary(v => v.Key, v => v.Value);
	}

	public Dictionary<string, string> Metadata {
		get { return _metadata; }
	}
}
