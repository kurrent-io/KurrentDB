// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.User;

public class IndexHelpersTests {
	[Theory]
	[InlineData("my-index", "$idx-user-my-index")]
	public void can_get_query_stream_name(string inputIndexName, string expectedStreamName) {
		var actualStreamName = UserIndexHelpers.GetQueryStreamName(inputIndexName);
		Assert.Equal(expectedStreamName, actualStreamName);
	}

	[Theory]
	[InlineData("$idx-user-my-index", "my-index", null)]
	[InlineData("$idx-user-my-index:country", "my-index", "country")]
	[InlineData("$idx-user-my-index:a=1;b=\"x\"", "my-index", "a=1;b=\"x\"")]
	public void can_parse_query_stream_name(string input, string expectedIndexName, string? expectedSuffix) {
		UserIndexHelpers.ParseQueryStreamName(input, out var actualIndexName, out var actualSuffix);
		Assert.Equal(expectedIndexName, actualIndexName);
		Assert.Equal(expectedSuffix, actualSuffix);
	}

	[Theory]
	[InlineData("my-index", "$UserIndex-my-index")]
	public void can_get_management_stream_name(string input, string expectedStreamName) {
		Assert.Equal(expectedStreamName, UserIndexHelpers.GetManagementStreamName(input));
	}

	private static IReadOnlyList<IField> Fields(params (string Name, IndexFieldType Type)[] fields) =>
		fields.Select(f => IField.Create(new IndexField { Name = f.Name, Selector = "e => e", Type = f.Type })).ToArray();

	[Fact]
	public void null_suffix_yields_no_constraints() {
		// "$idx-user-<name>" (no ':') -> whole index
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("country", IndexFieldType.String)), null, out var constraints);
		Assert.True(ok);
		Assert.Empty(constraints);
	}

	[Fact]
	public void empty_suffix_filters_a_single_field_by_empty_value() {
		// "$idx-user-<name>:" (trailing ':') -> legacy filter for the empty value, not the whole index
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("country", IndexFieldType.String)), "", out var constraints);
		Assert.True(ok);
		var constraint = Assert.Single(constraints);
		Assert.Equal("country", constraint.Field.Name);
		Assert.Equal("", constraint.Value);
	}

	[Fact]
	public void empty_suffix_rejected_for_multi_field_index() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("a", IndexFieldType.String), ("b", IndexFieldType.String)), "", out _);
		Assert.False(ok);
	}

	[Fact]
	public void legacy_bare_value_is_the_single_field_value() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("country", IndexFieldType.String)), "USA", out var constraints);
		Assert.True(ok);
		var constraint = Assert.Single(constraints);
		Assert.Equal("country", constraint.Field.Name);
		Assert.Equal("USA", constraint.Value);
	}

	[Fact]
	public void legacy_bare_value_rejected_for_multi_field_index() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("a", IndexFieldType.String), ("b", IndexFieldType.String)), "USA", out _);
		Assert.False(ok);
	}

	[Fact]
	public void parses_multiple_field_constraints_in_any_order() {
		var fields = Fields(("country", IndexFieldType.String), ("age", IndexFieldType.Int32));
		var ok = UserIndexHelpers.TryParseConstraints(fields, "age=42;country=\"USA\"", out var constraints);
		Assert.True(ok);
		Assert.Equal(2, constraints.Count);
		Assert.Equal("age", constraints[0].Field.Name);
		Assert.Equal("42", constraints[0].Value);
		Assert.Equal("country", constraints[1].Field.Name);
		Assert.Equal("USA", constraints[1].Value);
	}

	[Fact]
	public void numeric_values_are_normalized() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("price", IndexFieldType.Double)), "price=1.0", out var constraints);
		Assert.True(ok);
		Assert.Equal("1", Assert.Single(constraints).Value);
	}

	[Fact]
	public void quoted_string_escapes_are_resolved() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("note", IndexFieldType.String)), "note=\"a;b=\\\"c\\\"\"", out var constraints);
		Assert.True(ok);
		Assert.Equal("a;b=\"c\"", Assert.Single(constraints).Value);
	}

	[Fact]
	public void unknown_field_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("country", IndexFieldType.String)), "missing=1", out _);
		Assert.False(ok);
	}

	[Fact]
	public void duplicate_field_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("age", IndexFieldType.Int32)), "age=1;age=2", out _);
		Assert.False(ok);
	}

	[Fact]
	public void unparseable_numeric_value_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("age", IndexFieldType.Int32)), "age=notanumber", out _);
		Assert.False(ok);
	}

	[Fact]
	public void missing_equals_in_a_pair_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("a", IndexFieldType.Int32), ("b", IndexFieldType.Int32)), "a=1;b", out _);
		Assert.False(ok);
	}

	[Theory]
	[InlineData("note=\"abc")] // unterminated quote
	[InlineData("note=\"abc\"x")] // junk after the closing quote
	[InlineData("note=x\"abc\"")] // opening quote not at the start of the value
	[InlineData("note=\"a\\bc\"")] // invalid escape sequence (\b)
	[InlineData("note=\"abc\\")] // dangling backslash
	public void malformed_quoting_is_rejected(string suffix) {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("note", IndexFieldType.String)), suffix, out _);
		Assert.False(ok);
	}

	[Fact]
	public void empty_quoted_string_is_accepted() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("note", IndexFieldType.String)), "note=\"\"", out var constraints);
		Assert.True(ok);
		Assert.Equal("", Assert.Single(constraints).Value);
	}

	[Fact]
	public void parses_a_subset_of_the_fields_on_a_multi_field_index() {
		var fields = Fields(("country", IndexFieldType.String), ("age", IndexFieldType.Int32));
		var ok = UserIndexHelpers.TryParseConstraints(fields, "age=42", out var constraints);
		Assert.True(ok);
		var constraint = Assert.Single(constraints);
		Assert.Equal("age", constraint.Field.Name);
		Assert.Equal("42", constraint.Value);
	}

	[Fact]
	public void unquoted_string_value_is_accepted() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("country", IndexFieldType.String)), "country=USA", out var constraints);
		Assert.True(ok);
		Assert.Equal("USA", Assert.Single(constraints).Value);
	}

	[Fact]
	public void negative_numeric_value_is_accepted() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("age", IndexFieldType.Int32)), "age=-5", out var constraints);
		Assert.True(ok);
		Assert.Equal("-5", Assert.Single(constraints).Value);
	}

	[Fact]
	public void out_of_range_numeric_value_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("age", IndexFieldType.Int32)), "age=9999999999", out _);
		Assert.False(ok);
	}

	[Fact]
	public void empty_unquoted_value_is_rejected() {
		var ok = UserIndexHelpers.TryParseConstraints(Fields(("note", IndexFieldType.String)), "note=", out _);
		Assert.False(ok);
	}
}
