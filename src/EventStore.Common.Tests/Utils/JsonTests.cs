// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.Common.Utils;

namespace EventStore.Common.Tests.Utils;

public class JsonTests {
	[Theory]
	[InlineData("""
		{
			"some": "actually",
			"correct": ["json", true, false, null]
		}
		""")]
	[InlineData("""
		// a comment
		{
			// b comment
			"foo": "bar", // cheeky trailing comma
			// c comment
		}
		// d comment
		""")]
	public void accepts_valid(string json) {
		Assert.True(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json)).IsValidUtf8Json());
	}

	[Theory]
	[InlineData("")]
	[InlineData("{} invalid")]
	[InlineData("""{ "foo": "bar", invalid }""")]
	[InlineData("""
		// comment
		{ "foo": "bar" }
		invalid
		""")]
	[InlineData("""
		{ "foo": "bar" }
		invalid
		""")]
	[InlineData("""
		{ "foo": "bar" }
		{ "foo": "bar" }
		""")]
	public void rejects_invalid(string invalidJson) {
		Assert.False(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(invalidJson)).IsValidUtf8Json());
	}

	[Theory]
	[InlineData(64, true)]
	[InlineData(65, false)]
	public void check_depth(int depth, bool isValid) {
		var json =
			new string('[', depth) +
			new string(']', depth);

		Assert.Equal(isValid, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json)).IsValidUtf8Json());
	}

	[Fact]
	public void rejects_bom() {
		var json = new byte[] {
			0xEF, 0xBB, 0xBF, // bom
			(byte)'{',
			(byte)'}',
		};
		Assert.False(new ReadOnlyMemory<byte>(json).IsValidUtf8Json());
	}

	[Fact]
	public void utf16_is_not_valid() {
		var json = """{ "foo": "bar" }""";
		Assert.False(new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(json)).IsValidUtf8Json());
	}
}
