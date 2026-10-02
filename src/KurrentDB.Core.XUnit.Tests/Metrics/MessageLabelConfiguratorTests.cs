// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Configuration;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Metrics;

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
	private static MetricsConfiguration.LabelMappingCase CreateMapping(string regex, string label) => new() {
		Regex = regex,
		Label = label,
	};

	private static string Resolve(string originalLabel, params MetricsConfiguration.LabelMappingCase[] mappings) =>
		MessageLabelConfigurator.ResolveLabel(originalLabel, mappings);

	// labels are resolved lazily and cached, so clear the cache to resolve them again
	private static void ResetLabels() {
		ReadAllForward.LabelStatic = null;
		ReadAllBackward.LabelStatic = null;
		ReadStreamForward.LabelStatic = null;
		ReadStreamBackward.LabelStatic = null;
	}

	[Fact]
	public void no_map() {
		ResetLabels();

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
	public void configured_map_applies_to_message_label() {
		// the configuration is process-wide, so use a mapping that only matches the test messages and restore it afterwards
		MessageLabelConfigurator.ConfigureMessageLabels([CreateMapping("TestGroup-Reads-ReadAll(.*)", "$1AllRead")]);
		try {
			ResetLabels();

			Assert.Equal("ForwardAllRead", new ReadAllForward().Label);
			Assert.Equal("ForwardAllRead", ReadAllForward.LabelStatic);
			Assert.Equal("TestGroup-Reads-ReadAllForward", ReadAllForward.OriginalLabelStatic);
			Assert.Equal("TestGroup-Reads-ReadStreamForward", new ReadStreamForward().Label);
		} finally {
			MessageLabelConfigurator.ConfigureMessageLabels([]);
			ResetLabels();
		}
	}

	[Fact]
	public void simple_map() {
		MetricsConfiguration.LabelMappingCase[] mappings = [
			CreateMapping("TestGroup-Reads-ReadAll.*", "ReadAll"),
			CreateMapping("TestGroup-Reads-ReadStream.*", "ReadStream"),
		];

		Assert.Equal("ReadAll", Resolve(ReadAllForward.OriginalLabelStatic, mappings));
		Assert.Equal("ReadAll", Resolve(ReadAllBackward.OriginalLabelStatic, mappings));
		Assert.Equal("ReadStream", Resolve(ReadStreamForward.OriginalLabelStatic, mappings));
		Assert.Equal("ReadStream", Resolve(ReadStreamBackward.OriginalLabelStatic, mappings));
	}

	[Fact]
	public void map_with_capture() {
		MetricsConfiguration.LabelMappingCase[] mappings = [
			CreateMapping("TestGroup-Reads-ReadAll(.*)", "$1AllRead"),
			CreateMapping("TestGroup-Reads-ReadStream(.*)", "$1StreamRead"),
		];

		Assert.Equal("ForwardAllRead", Resolve(ReadAllForward.OriginalLabelStatic, mappings));
		Assert.Equal("BackwardAllRead", Resolve(ReadAllBackward.OriginalLabelStatic, mappings));
		Assert.Equal("ForwardStreamRead", Resolve(ReadStreamForward.OriginalLabelStatic, mappings));
		Assert.Equal("BackwardStreamRead", Resolve(ReadStreamBackward.OriginalLabelStatic, mappings));
	}

	[Fact]
	public void cases_matched_in_order() {
		MetricsConfiguration.LabelMappingCase[] mappings = [
			CreateMapping(".*Forward.*", "Forward"),
			CreateMapping(".*Stream.*", "Stream"),
			CreateMapping(".*", "Other"),
		];

		Assert.Equal("Forward", Resolve(ReadAllForward.OriginalLabelStatic, mappings));
		Assert.Equal("Other", Resolve(ReadAllBackward.OriginalLabelStatic, mappings));
		Assert.Equal("Forward", Resolve(ReadStreamForward.OriginalLabelStatic, mappings));
		Assert.Equal("Stream", Resolve(ReadStreamBackward.OriginalLabelStatic, mappings));
	}

	[Fact]
	public void unspecified_label() {
		var mapping = new MetricsConfiguration.LabelMappingCase() {
			Regex = "TestGroup-Reads-ReadAll.*",
			// no Label
		};

		Assert.Equal("TestGroup-Reads-ReadAllForward", Resolve(ReadAllForward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadAllBackward", Resolve(ReadAllBackward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadStreamForward", Resolve(ReadStreamForward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", Resolve(ReadStreamBackward.OriginalLabelStatic, mapping));
	}

	[Fact]
	public void unspecified_regex() {
		var mapping = new MetricsConfiguration.LabelMappingCase() {
			// no Regex
			Label = "TheLabel",
		};

		Assert.Equal("TestGroup-Reads-ReadAllForward", Resolve(ReadAllForward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadAllBackward", Resolve(ReadAllBackward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadStreamForward", Resolve(ReadStreamForward.OriginalLabelStatic, mapping));
		Assert.Equal("TestGroup-Reads-ReadStreamBackward", Resolve(ReadStreamBackward.OriginalLabelStatic, mapping));
	}
}
