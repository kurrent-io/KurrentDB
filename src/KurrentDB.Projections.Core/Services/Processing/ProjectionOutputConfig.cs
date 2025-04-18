// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.Serialization;

namespace KurrentDB.Projections.Core.Services.Processing;

[DataContract]
public class ProjectionOutputConfig {
	[DataMember] public string ResultStreamName { get; set; }
}
