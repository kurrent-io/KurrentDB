// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using ObjectLayoutInspector;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.LogV3.Tests;

// For easily inspecting the layout of the structs
public class LayoutInspections {
	private readonly ITestOutputHelper _output;

	public LayoutInspections(ITestOutputHelper output) {
		_output = output;
	}

	[Theory]
	[InlineData(typeof(Raw.RecordHeader))]
	[InlineData(typeof(Raw.EpochHeader))]
	[InlineData(typeof(Raw.EventHeader))]
	[InlineData(typeof(Raw.PartitionHeader))]
	[InlineData(typeof(Raw.PartitionTypeHeader))]
	[InlineData(typeof(Raw.StreamHeader))]
	[InlineData(typeof(Raw.StreamTypeHeader))]
	[InlineData(typeof(Raw.StreamWriteHeader))]
	[InlineData(typeof(Raw.EventTypeHeader))]
	[InlineData(typeof(Raw.ContentTypeHeader))]
	[InlineData(typeof(Raw.TransactionStartHeader))]
	[InlineData(typeof(Raw.TransactionEndHeader))]
	public void InspectLayout(Type t) {
		var layout = TypeLayout.GetLayout(t);
		_output.WriteLine(layout.ToString(recursively: false));
	}
}
