// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Embeddings.Normalization;

namespace Kurrent.Kontext.Embeddings.Tests;

public class JsonNormalizerTests {
	[Test]
	public async ValueTask flattens_an_object_to_split_key_value_pairs() {
		// Arrange
		var expected =
			"""
			tool name: bash,
			command line: dotnet test --filter ReadTests,
			exit code: 0,
			duration ms: 5400
			""";

		// Act
		var normalized = JsonNormalizer.Instance.Normalize(
			"""{"toolName":"bash","commandLine":"dotnet test --filter ReadTests","exitCode":0,"durationMs":5400}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask passes_non_json_through_unchanged() {
		// Arrange
		const string prose = "the reactor calibration completed without incident";

		// Act + Assert
		await Assert.That(JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(prose))).IsEqualTo(prose);
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("\r\n\t ")]
	public async ValueTask returns_null_when_there_is_nothing_to_normalize(string payload) {
		// Act + Assert
		await Assert.That(JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(payload))).IsNull();
	}

	[Test]
	public async ValueTask passes_malformed_json_through_unchanged() {
		// Arrange
		const string broken = """{"toolName": "bash", "unterminated""";

		// Act + Assert
		await Assert.That(JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(broken))).IsEqualTo(broken);
	}

	[Test]
	public async ValueTask skips_booleans_and_nulls() {
		// Act
		var normalized = JsonNormalizer.Instance.Normalize("""{"express":true,"error":null,"total":19.90}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo("total: 19.90");
	}

	[Test]
	public async ValueTask recurses_nested_objects_using_their_own_keys() {
		// Arrange
		var expected =
			"""
			name: Ripley,
			city: Lisbon
			""";

		// Act
		var normalized = JsonNormalizer.Instance.Normalize("""{"customer":{"name":"Ripley","city":"Lisbon"}}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask joins_scalar_arrays_under_one_key() {
		// Act
		var normalized = JsonNormalizer.Instance.Normalize("""{"tags":["kontext","lance","fts"]}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo("tags: kontext lance fts");
	}

	[Test]
	public async ValueTask emits_object_elements_of_arrays_with_their_own_keys() {
		// Arrange
		var expected =
			"""
			name: Ripley,
			name: Dallas
			""";

		// Act
		var normalized = JsonNormalizer.Instance.Normalize("""{"crew":[{"name":"Ripley"},{"name":"Dallas"}]}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask splits_snake_and_kebab_keys_like_camel_case() {
		// Arrange
		var expected =
			"""
			session id: s-77,
			created at: 1234
			""";

		// Act
		var normalized = JsonNormalizer.Instance.Normalize("""{"session_id":"s-77","created-at":1234}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask keeps_punctuation_inside_values_verbatim() {
		// Arrange — values may carry commas and colons; the rendering is never parsed back,
		// so nothing escapes or quotes them.
		const string expected = "error message: connection refused: retry 1, retry 2, giving up";

		// Act
		var normalized = JsonNormalizer.Instance.Normalize(
			"""{"errorMessage":"connection refused: retry 1, retry 2, giving up"}"""u8);

		// Assert
		await Assert.That(normalized).IsEqualTo(expected);
	}
}
