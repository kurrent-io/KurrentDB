// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Reflection;
using EventStore.Common.Configuration;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

enum TestGroup {
	Reads,
}

[DerivedMessage]
abstract partial class ReadMessage : Message { }

[DerivedMessage(TestGroup.Reads)]
partial class ReadAllForward : ReadMessage { }

[DerivedMessage(TestGroup.Reads)]
partial class ReadAllBackward : ReadMessage { }

[DerivedMessage(TestGroup.Reads)]
partial class ReadStreamForward : ReadMessage { }

[DerivedMessage(TestGroup.Reads)]
partial class ReadStreamBackward : ReadMessage { }

[Collection("MetricsLabelTests")] // labels are static
public class MessageLabelConfiguratorTests {
	private readonly Type[] _messageTypes;

	public MessageLabelConfiguratorTests() {
		_messageTypes = new[] {
			typeof(ReadMessage),
			typeof(ReadAllForward),
			typeof(ReadAllBackward),
			typeof(ReadStreamForward),
			typeof(ReadStreamBackward),
		};
	}

	private MetricsConfiguration.LabelMappingCase CreateMapping(string regex, string label) => new() {
		Regex = regex,
		Label = label,
	};

	private void ResetLabels() {
		var flags = BindingFlags.Static | BindingFlags.Public;
		foreach (var type in _messageTypes) {
			var labelProperty = type.GetProperty("LabelStatic", flags);
			var originalLabelProperty = type.GetProperty("OriginalLabelStatic", flags);

			labelProperty?.SetValue(null, originalLabelProperty.GetValue(null));
		}
	}

	private void Run(params MetricsConfiguration.LabelMappingCase[] mappings) {
		ResetLabels();
		MessageLabelConfigurator.ConfigureMessageLabels(mappings, _messageTypes);
	}

	[Fact]
	public void no_map() {
		Run();

		Assert.Equal("TestGroup-Reads-ReadAllForward", ReadAllForward.LabelStatic);
		Assert.Equal("TestGroup-Reads-ReadAllForward", ReadAllForward.OriginalLabelStatic);
		Assert.Equal("TestGroup-Reads-ReadAllForward", new ReadAllForward().Label);

		Assert.Equal("TestGroup-Reads-ReadAllBackward", ReadAllBackward.LabelStatic);
		Assert.Equal("TestGroup-Reads-ReadAllBackward", ReadAllBackward.OriginalLabelStatic);
		Assert.Equal("TestGroup-Reads-ReadAllBackward", new ReadAllBackward().Label);

		Assert.Equal("TestGroup-Reads-ReadStreamForward", ReadStreamForward.LabelStatic);
		Assert.Equal("TestGroup-Reads-ReadStreamForward", ReadStreamForward.OriginalLabelStatic);
		Assert.Equal("TestGroup-Reads-ReadStreamForward", new ReadStreamForward().Label);

		Assert.Equal("TestGroup-Reads-ReadStreamBackward", ReadStreamBackward.LabelStatic);
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", ReadStreamBackward.OriginalLabelStatic);
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", new ReadStreamBackward().Label);
	}

	[Fact]
	public void simple_map() {
		Run(
			CreateMapping("TestGroup-Reads-ReadAll.*", "ReadAll"),
			CreateMapping("TestGroup-Reads-ReadStream.*", "ReadStream"));

		Assert.Equal("ReadAll", new ReadAllForward().Label);
		Assert.Equal("ReadAll", new ReadAllBackward().Label);
		Assert.Equal("ReadStream", new ReadStreamForward().Label);
		Assert.Equal("ReadStream", new ReadStreamBackward().Label);
	}

	[Fact]
	public void map_with_capture() {
		Run(
			CreateMapping("TestGroup-Reads-ReadAll(.*)", "$1AllRead"),
			CreateMapping("TestGroup-Reads-ReadStream(.*)", "$1StreamRead"));

		Assert.Equal("ForwardAllRead", new ReadAllForward().Label);
		Assert.Equal("BackwardAllRead", new ReadAllBackward().Label);
		Assert.Equal("ForwardStreamRead", new ReadStreamForward().Label);
		Assert.Equal("BackwardStreamRead", new ReadStreamBackward().Label);
	}

	[Fact]
	public void cases_matched_in_order() {
		Run(
			CreateMapping(".*Forward.*", "Forward"),
			CreateMapping(".*Stream.*", "Stream"),
			CreateMapping(".*", "Other"));

		Assert.Equal("Forward", new ReadAllForward().Label);
		Assert.Equal("Other", new ReadAllBackward().Label);
		Assert.Equal("Forward", new ReadStreamForward().Label);
		Assert.Equal("Stream", new ReadStreamBackward().Label);
	}

	[Fact]
	public void unspecified_label() {
		Run(new MetricsConfiguration.LabelMappingCase() {
			Regex = "TestGroup-Reads-ReadAll.*",
			// no Label
		});

		Assert.Equal("TestGroup-Reads-ReadAllForward", new ReadAllForward().Label);
		Assert.Equal("TestGroup-Reads-ReadAllBackward", new ReadAllBackward().Label);
		Assert.Equal("TestGroup-Reads-ReadStreamForward", new ReadStreamForward().Label);
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", new ReadStreamBackward().Label);
	}

	[Fact]
	public void unspecified_regex() {
		Run(new MetricsConfiguration.LabelMappingCase() {
			// no Regex
			Label = "TheLabel",
		});

		Assert.Equal("TestGroup-Reads-ReadAllForward", new ReadAllForward().Label);
		Assert.Equal("TestGroup-Reads-ReadAllBackward", new ReadAllBackward().Label);
		Assert.Equal("TestGroup-Reads-ReadStreamForward", new ReadStreamForward().Label);
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", new ReadStreamBackward().Label);
	}
}
