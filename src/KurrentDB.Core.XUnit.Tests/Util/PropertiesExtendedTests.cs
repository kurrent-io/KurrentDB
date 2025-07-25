// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protobuf;
using KurrentDB.Protobuf.Server;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Util;

public class PropertiesExtendedTests {
	[Fact]
	public void can_convert_all_types_to_synthetic_metadata() {
		// given
		var now = new DateTime(2025, 07, 14, 05, 05, 05, DateTimeKind.Utc);

		var properties = new Properties {
			PropertiesValues = {
				{ "my-null", new() { NullValue = NullValue.NullValue } },
				{ "my-int32", new() { Int32Value = 32 } },
				{ "my-int64", new() { Int64Value = 64 } },
				{ "my-bytes", new() { BytesValue = ByteString.CopyFromUtf8("utf8-bytes") } },
				{ "my-float", new() { FloatValue = 123.4f } },
				{ "my-double", new() { DoubleValue = 567.8 } },
				{ "my-boolean", new() { BooleanValue = true } },
				{ "my-string", new() { StringValue = "hello-world" } },
				{ "my-timestamp", new() { TimestampValue = Timestamp.FromDateTime(now) } },
				{ "my-duration", new() { DurationValue = Duration.FromTimeSpan(TimeSpan.FromSeconds(121)) } },
			}
		};

		// when
		var syntheticMetadata = properties.SerializeToBytes().Span;

		// then
		var expected = $$"""
			{
			  "my-null":      null,
			  "my-int32":     32,
			  "my-int64":     64,
			  "my-bytes":     "{{Convert.ToBase64String(Encoding.UTF8.GetBytes("utf8-bytes"))}}",
			  "my-float":     123.4,
			  "my-double":    567.8,
			  "my-boolean":   true,
			  "my-string":    "hello-world",
			  "my-timestamp": "2025-07-14T05:05:05Z",
			  "my-duration":  "00:02:01"
			}
			""";

		var actual = Encoding.UTF8.GetString(syntheticMetadata);
		Assert.Equal(expected.Replace(" ", "").Replace(Environment.NewLine, ""), actual);
	}

	[Fact]
	public void preserves_key_names() {
		// given
		var x = new DynamicValue { Int32Value = 32 };

		var properties = new Properties {
			PropertiesValues = {
				{ "camelCase", x },
				{ "kebab-case", x },
				{ "PascalCase", x },
				{ "snake_case", x },
				{ "ANGRY_SNAKE_CASE", x },
			}
		};

		// when
		var syntheticMetadata = properties.SerializeToBytes().Span;

		// then
		var expected = """
			{
			  "camelCase": 32,
			  "kebab-case": 32,
			  "PascalCase": 32,
			  "snake_case": 32,
			  "ANGRY_SNAKE_CASE": 32
			}
			""";

		var actual = Encoding.UTF8.GetString(syntheticMetadata);
		Assert.Equal(expected.Replace(" ", "").Replace(Environment.NewLine, ""), actual);
	}

	[Fact]
	public void allows_named_floating_point_literals() {
		// given
		var properties = new Properties {
			PropertiesValues = {
				{ "my-float-nan", new() { FloatValue = float.NaN } },
				{ "my-float-positive-infinity", new() { FloatValue = float.PositiveInfinity } },
				{ "my-float-negative-infinity", new() { FloatValue = float.NegativeInfinity } },
				{ "my-double-nan", new() { DoubleValue = double.NaN } },
				{ "my-double-positive-infinity", new() { DoubleValue = double.PositiveInfinity } },
				{ "my-double-negative-infinity", new() { DoubleValue = double.NegativeInfinity } },
			}
		};

		// when
		var syntheticMetadata = properties.SerializeToBytes().Span;

		// then
		var expected = """
			{
			  "my-float-nan": "NaN",
			  "my-float-positive-infinity": "Infinity",
			  "my-float-negative-infinity": "-Infinity",
			  "my-double-nan": "NaN",
			  "my-double-positive-infinity": "Infinity",
			  "my-double-negative-infinity": "-Infinity"
			}
			""";

		var actual = Encoding.UTF8.GetString(syntheticMetadata);
		Assert.Equal(expected.Replace(" ", "").Replace(Environment.NewLine, ""), actual);
	}}
