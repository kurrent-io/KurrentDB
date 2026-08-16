// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Pins the attach-rendering contract of <see cref="DuckDBDataSourceOptions"/>: the ATTACH
/// options list is a generic key/value pass-through — core keys and extension keys ride the same
/// validated path (probed engine-side: an unconsumed key fails the attach with "Unrecognized
/// option for attach", so nothing here needs a key catalog).
/// </summary>
public class DuckDBDataSourceOptionsTests {
	[Test]
	public async ValueTask attach_renders_bare_when_nothing_is_specified() {
		// Arrange
		var options  = new DuckDBDataSourceOptions().AttachDatabase("lance:/data/ldb", "ldb");
		var expected = "ATTACH IF NOT EXISTS 'lance:/data/ldb' AS ldb;";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_renders_the_database_type_bare() {
		// Arrange
		var options  = new DuckDBDataSourceOptions().AttachDatabase("/data/ldb", "ldb", attach => attach.Type("LANCE"));
		var expected = "ATTACH IF NOT EXISTS '/data/ldb' AS ldb (TYPE LANCE);";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_renders_read_only() {
		// Arrange
		var options  = new DuckDBDataSourceOptions().AttachDatabase("/data/ldb", "ldb", attach => attach.ReadOnly());
		var expected = "ATTACH IF NOT EXISTS '/data/ldb' AS ldb (READ_ONLY);";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_renders_the_options_list_in_registration_order() {
		// Arrange — core key, extension key, core flag: one list, one parenthesized set.
		var options = new DuckDBDataSourceOptions().AttachDatabase(
			"/data/ldb", "ldb",
			attach => attach.Type("LANCE").Option("ENDPOINT", "http://namespace:8080").ReadOnly());

		var expected = "ATTACH IF NOT EXISTS '/data/ldb' AS ldb (TYPE LANCE, ENDPOINT 'http://namespace:8080', READ_ONLY);";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_renders_numeric_options_bare() {
		// Arrange
		var options  = new DuckDBDataSourceOptions().AttachDatabase("/data/file.db", "db", attach => attach.Option("BLOCK_SIZE", 16384));
		var expected = "ATTACH IF NOT EXISTS '/data/file.db' AS db (BLOCK_SIZE 16384);";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_escapes_quotes_in_path_and_string_values() {
		// Arrange
		var options = new DuckDBDataSourceOptions().AttachDatabase(
			"/data/it's/ldb", "ldb",
			attach => attach.Option("BEARER_TOKEN", "tok'en"));

		var expected = "ATTACH IF NOT EXISTS '/data/it''s/ldb' AS ldb (BEARER_TOKEN 'tok''en');";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_renders_a_raw_options_string_verbatim() {
		// Arrange — the raw overload: the whole fragment is the caller's, untouched.
		var options  = new DuckDBDataSourceOptions().AttachDatabase("/data/ldb", "ldb", "TYPE LANCE, ENDPOINT 'http://namespace:8080'");
		var expected = "ATTACH IF NOT EXISTS '/data/ldb' AS ldb (TYPE LANCE, ENDPOINT 'http://namespace:8080');";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask raw_fragments_mix_with_typed_entries() {
		// Arrange
		var options  = new DuckDBDataSourceOptions().AttachDatabase("/data/ldb", "ldb", attach => attach.Raw("TYPE LANCE").ReadOnly());
		var expected = "ATTACH IF NOT EXISTS '/data/ldb' AS ldb (TYPE LANCE, READ_ONLY);";

		// Act
		var rendered = options.GenerateSqlStatements();

		// Assert
		await Assert.That(rendered.AttachDatabases).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask attach_option_keys_must_be_bare_identifiers() {
		// Arrange — the injection shape: a key carrying its own SQL is rejected at Add time,
		// never escaped.
		var options = new DuckDBAttachOptions();

		// Act
		ArgumentException? exception = null;
		try {
			options.Option("ENDPOINT 'x', BOGUS", "value");
		} catch (ArgumentException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
	}

	[Test]
	public async ValueTask attach_type_must_be_a_bare_identifier() {
		// Arrange
		var options = new DuckDBAttachOptions();

		// Act
		ArgumentException? exception = null;
		try {
			options.Type("LANCE, READ_ONLY");
		} catch (ArgumentException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
	}

	[Test]
	public async ValueTask extension_loads_render_before_attachments() {
		// Arrange — a typed attach only works when its extension is already loaded, so the
		// per-connection script must keep loads ahead of attachments.
		var options = new DuckDBDataSourceOptions()
			.Extensions(extensions => extensions.Load("lance"))
			.AttachDatabase("/data/ldb", "ldb", attach => attach.Type("LANCE"));

		// Act
		var script = options.GenerateSqlStatements().ForConnection;

		// Assert
		var loadIndex   = script.IndexOf("LOAD lance;", StringComparison.Ordinal);
		var attachIndex = script.IndexOf("ATTACH IF NOT EXISTS", StringComparison.Ordinal);
		await Assert.That(loadIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(attachIndex).IsGreaterThan(loadIndex);
	}
}
