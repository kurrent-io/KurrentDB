// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Kurrent.Quack;

namespace KurrentDB.SchemaRegistry.Data;

// Quack-based queries for SchemaQueries
// These are not yet wired into SchemaQueries.cs but provide efficient, reflection-free
// query definitions that can be used to replace Dapper-based calls.
internal static class SchemaSql {
	public readonly record struct GetByNameArgs(string SchemaName);

	public readonly record struct GetByVersionIdArgs(string SchemaVersionId);

	public readonly record struct GetByNameAndNumberArgs(string SchemaName, int VersionNumber);

	public readonly record struct ListSchemasArgs(string SchemaNamePrefix, string TagsJson);

	public readonly record struct SchemaRow(
		string SchemaName,
		string Description,
		int DataFormat,
		int LatestVersionNumber,
		string LatestVersionId,
		int Compatibility,
		string Tags,
		DateTime CreatedAt,
		DateTime UpdatedAt
	);

	public readonly record struct SchemaVersionRow(
		string VersionId,
		int VersionNumber,
		string SchemaDefinition,
		int DataFormat,
		DateTime RegisteredAt
	);

	public readonly record struct RegisteredSchemaRow(
		string SchemaName,
		int DataFormat,
		int Compatibility,
		string Tags,
		string VersionId,
		int VersionNumber,
		string SchemaDefinition,
		DateTime RegisteredAt
	);

	public readonly record struct SchemaValidationInfoRow(
		string VersionId,
		string SchemaDefinition,
		int DataFormat,
		int Compatibility
	);

	// SELECT * FROM schemas WHERE schema_name = $schema_name
	public struct GetSchemaByNameQuery : IQuery<GetByNameArgs, SchemaRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select schema_name, description, data_format, latest_version_number, latest_version_id, compatibility, tags, created_at, updated_at from schemas where schema_name=$1"u8;

		public static SchemaRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadDateTime(),
				row.ReadDateTime()
			);
	}

	// SELECT schema_name FROM schema_versions WHERE version_id = $schema_version_id
	public readonly record struct LookupSchemaNameRow(string SchemaName);

	public struct LookupSchemaNameByVersionIdQuery : IQuery<GetByVersionIdArgs, LookupSchemaNameRow> {
		public static BindingContext Bind(in GetByVersionIdArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaVersionId };

		public static ReadOnlySpan<byte> CommandText =>
			"select schema_name from schema_versions where version_id=$1"u8;

		public static LookupSchemaNameRow Parse(ref DataChunk.Row row) => new(row.ReadString());
	}

	// Get schema version by name and version number
	public struct GetSchemaVersionByNameAndNumberQuery : IQuery<GetByNameAndNumberArgs, SchemaVersionRow> {
		public static BindingContext Bind(in GetByNameAndNumberArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName, args.VersionNumber };

		public static ReadOnlySpan<byte> CommandText =>
			"select version_id, version_number, decode(schema_definition) as schema_definition, data_format, registered_at from schema_versions where schema_name=$1 and version_number=$2"u8;

		public static SchemaVersionRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadDateTime()
			);
	}

	// Get latest schema version by name
	public struct GetLatestSchemaVersionByNameQuery : IQuery<GetByNameArgs, SchemaVersionRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select version_id, version_number, decode(schema_definition) as schema_definition, data_format, registered_at from schema_versions where schema_name=$1 order by version_number desc limit 1"u8;

		public static SchemaVersionRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadDateTime()
			);
	}

	// Get schema version by id
	public struct GetSchemaVersionByIdQuery : IQuery<GetByVersionIdArgs, SchemaVersionRow> {
		public static BindingContext Bind(in GetByVersionIdArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaVersionId };

		public static ReadOnlySpan<byte> CommandText =>
			"select version_id, version_number, decode(schema_definition) as schema_definition, data_format, registered_at from schema_versions where version_id=$1"u8;

		public static SchemaVersionRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadDateTime()
			);
	}

	// List schemas with optional name prefix and tags filter
	public struct ListSchemasQuery : IQuery<ListSchemasArgs, SchemaRow> {
		public static BindingContext Bind(in ListSchemasArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaNamePrefix, args.TagsJson };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select
				schema_name,
				description,
				data_format,
				latest_version_number,
				latest_version_id,
				compatibility,
				tags,
				created_at,
				updated_at
			from
				schemas
			where
				($1='' or schema_name ilike $1) and
				($2='' or json_contains(tags, $2))
			"""u8;

		public static SchemaRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadDateTime(),
				row.ReadDateTime()
			);
	}

	// List schema versions (include definition)
	public struct ListSchemaVersionsIncludeDefinitionQuery : IQuery<GetByNameArgs, SchemaVersionRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select version_id, version_number, decode(schema_definition) as schema_definition, data_format, registered_at from schema_versions where schema_name=$1 order by version_number"u8;

		public static SchemaVersionRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadDateTime()
			);
	}

	// List schema versions (exclude definition)
	public readonly record struct SchemaVersionHeaderRow(
		string VersionId,
		int VersionNumber,
		int DataFormat,
		DateTime RegisteredAt
	);

	public struct ListSchemaVersionsExcludeDefinitionQuery : IQuery<GetByNameArgs, SchemaVersionHeaderRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select version_id, version_number, data_format, registered_at from schema_versions where schema_name=$1 order by version_number"u8;

		public static SchemaVersionHeaderRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32(),
				row.ReadDateTime()
			);
	}

	// Since we need a 3-argument version id/prefix/tags variant, define a dedicated args and query
	public readonly record struct ListRegisteredSchemasArgs(string SchemaVersionId, string SchemaNamePrefix, string TagsJson);

	public struct ListRegisteredSchemasByArgsQuery : IQuery<ListRegisteredSchemasArgs, RegisteredSchemaRow> {
		public static BindingContext Bind(in ListRegisteredSchemasArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaVersionId, args.SchemaNamePrefix, args.TagsJson };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			select
				s.schema_name,
				s.data_format,
				s.compatibility,
				s.tags,
				v.version_id,
				v.version_number,
				decode(v.schema_definition) as schema_definition,
				v.registered_at
			from
				schemas s
				inner join schema_versions v on s.latest_version_id = v.version_id
			where
				($1='' or v.version_id=$1) and
				($2='' or s.schema_name ilike $2) and
				($3='' or json_contains(s.tags, $3::json))
			"""u8;

		public static RegisteredSchemaRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadString(),
				row.ReadDateTime()
			);
	}

	// Validation info by schema name (latest)
	public struct GetLatestSchemaValidationInfoByNameQuery : IQuery<GetByNameArgs, SchemaValidationInfoRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select v.version_id, decode(v.schema_definition) as schema_definition, v.data_format, s.compatibility from schemas s inner join schema_versions v on v.version_id = s.latest_version_id where s.schema_name=$1"u8;

		public static SchemaValidationInfoRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32()
			);
	}

	// Validation info by schema version id (latest for that schema)
	public struct GetLatestSchemaValidationInfoByVersionIdQuery : IQuery<GetByVersionIdArgs, SchemaValidationInfoRow> {
		public static BindingContext Bind(in GetByVersionIdArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaVersionId };

		public static ReadOnlySpan<byte> CommandText =>
			"select v.version_id, decode(v.schema_definition) as schema_definition, v.data_format, s.compatibility from schemas s inner join schema_versions v on v.version_id = s.latest_version_id where s.schema_name=(select schema_name from schema_versions where version_id=$1)"u8;

		public static SchemaValidationInfoRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32()
			);
	}

	// All validation infos by schema name
	public struct GetAllSchemaValidationInfosByNameQuery : IQuery<GetByNameArgs, SchemaValidationInfoRow> {
		public static BindingContext Bind(in GetByNameArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaName };

		public static ReadOnlySpan<byte> CommandText =>
			"select v.version_id, decode(v.schema_definition) as schema_definition, v.data_format, s.compatibility from schemas s inner join schema_versions v on v.schema_name = s.schema_name where s.schema_name=$1 order by v.version_number"u8;

		public static SchemaValidationInfoRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32()
			);
	}

	// All validation infos by schema version id
	public struct GetAllSchemaValidationInfosByVersionIdQuery : IQuery<GetByVersionIdArgs, SchemaValidationInfoRow> {
		public static BindingContext Bind(in GetByVersionIdArgs args, PreparedStatement statement)
			=> new(statement) { args.SchemaVersionId };

		public static ReadOnlySpan<byte> CommandText =>
			"select v.version_id, decode(v.schema_definition) as schema_definition, v.data_format, s.compatibility from schemas s inner join schema_versions v on v.schema_name = s.schema_name where s.schema_name=(select schema_name from schema_versions where version_id=$1) order by v.version_number"u8;

		public static SchemaValidationInfoRow Parse(ref DataChunk.Row row)
			=> new(
				row.ReadString(),
				row.ReadString(),
				row.ReadInt32(),
				row.ReadInt32()
			);
	}
}
