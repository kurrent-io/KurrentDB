// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.ClientAPI;

namespace EventStore.Projections.Core.Tests.ClientAPI;

internal static class RecordedEventExtensions {
	public static string DebugDataView(this RecordedEvent source) => Encoding.UTF8.GetString(source.Data);
	public static string DebugMetadataView(this RecordedEvent source) => Encoding.UTF8.GetString(source.Metadata);
}
