// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.AutoScavenge.Converters;

namespace KurrentDB.AutoScavenge;

[JsonConverter(typeof(EventJsonConverter))]
public interface IEvent {
	string Type { get; }
}
