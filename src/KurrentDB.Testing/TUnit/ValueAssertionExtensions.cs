// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Encodings.Web;
using System.Text.Json;
using TUnit.Assertions.Conditions;
using TUnit.Assertions.Sources;

namespace KurrentDB.Testing.TUnit;

public static class ValueAssertionExtensions {
	static readonly JsonSerializerOptions SerializerOptions = new() {
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	static string Normalize(string json) => JsonSerializer.Serialize(
		JsonDocument.Parse(json),
		SerializerOptions);

	public static StringEqualsAssertion<string> IsJson(this ValueAssertion<string> source, string expectedJson) =>
		new(source.Context.Map(actualJson => Normalize(actualJson!)), Normalize(expectedJson!));
}
