// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public class ExtraMetaData {
	public ExtraMetaData(Dictionary<string, JRaw> metadata) {
		Metadata = metadata.ToDictionary(v => v.Key, v => v.Value.ToString());
	}

	public ExtraMetaData(Dictionary<string, string> metadata) {
		Metadata = metadata.ToDictionary(v => v.Key, v => v.Value);
	}

	public Dictionary<string, string> Metadata { get; }
}
