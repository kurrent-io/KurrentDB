// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Records.Mcp;
using KurrentDB.Testing.Bogus;
using SearchOptions = Kurrent.Kontext.Records.Mcp.Model.SearchOptions;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// The MCP edge collapses four optional members into the contract's two oneofs. JSON schema cannot state
/// that exclusivity, so the mapper is the only place it can be enforced — and protobuf would otherwise
/// keep whichever half was assigned last and answer under a filter nobody chose.
/// </summary>
public class McpRecordMappersTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async ValueTask maps_a_stream_filter_onto_the_scope() {
        // Arrange
        var expectedStream = Faker.Random.AlphaNumeric(12);
        var query          = Faker.Lorem.Sentence();

        // Act
        var request = McpRecordMappers.ToContract(query, new SearchOptions { Stream = expectedStream });

        // Assert
        await Assert.That(request.ScopeCase).IsEqualTo(Contracts.SearchRequest.ScopeOneofCase.Stream);
        await Assert.That(request.Stream).IsEqualTo(expectedStream);
    }

    [Test]
    public async ValueTask maps_a_category_filter_onto_the_scope() {
        // Arrange
        var expectedCategory = Faker.Random.AlphaNumeric(8);
        var query            = Faker.Lorem.Sentence();

        // Act
        var request = McpRecordMappers.ToContract(query, new SearchOptions { Category = expectedCategory });

        // Assert
        await Assert.That(request.ScopeCase).IsEqualTo(Contracts.SearchRequest.ScopeOneofCase.Category);
        await Assert.That(request.Category).IsEqualTo(expectedCategory);
    }

    [Test]
    public async ValueTask maps_a_schema_name_filter_onto_the_schema() {
        // Arrange
        var expectedSchemaName = Faker.Random.AlphaNumeric(10);
        var query              = Faker.Lorem.Sentence();

        // Act
        var request = McpRecordMappers.ToContract(query, new SearchOptions { SchemaName = expectedSchemaName });

        // Assert — the filter agents reach for most, so it must survive the collapse into the oneof.
        await Assert.That(request.SchemaCase).IsEqualTo(Contracts.SearchRequest.SchemaOneofCase.SchemaName);
        await Assert.That(request.SchemaName).IsEqualTo(expectedSchemaName);
    }

    [Test]
    public async ValueTask leaves_the_scope_unset_when_no_filter_is_given() {
        // Arrange
        var query = Faker.Lorem.Sentence();

        // Act
        var request = McpRecordMappers.ToContract(query, new SearchOptions());

        // Assert — an unfiltered search ranks across the whole log, which is the documented default.
        await Assert.That(request.ScopeCase).IsEqualTo(Contracts.SearchRequest.ScopeOneofCase.None);
        await Assert.That(request.SchemaCase).IsEqualTo(Contracts.SearchRequest.SchemaOneofCase.None);
    }

    [Test]
    public async ValueTask rejects_a_stream_and_a_category_together() {
        // Arrange
        var options = new SearchOptions {
            Stream   = Faker.Random.AlphaNumeric(12),
            Category = Faker.Random.AlphaNumeric(8),
        };

        var query = Faker.Lorem.Sentence();

        // Act
        var map = () => McpRecordMappers.ToContract(query, options);

        // Assert — silently keeping one would answer a question the caller never asked.
        await Assert.That(map).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask rejects_a_schema_id_and_a_schema_format_together() {
        // Arrange
        var options = new SearchOptions {
            SchemaId     = Faker.Random.Guid().ToString(),
            SchemaFormat = "Json",
        };

        var query = Faker.Lorem.Sentence();

        // Act
        var map = () => McpRecordMappers.ToContract(query, options);

        // Assert
        await Assert.That(map).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask folds_query_rows_into_the_model() {
        // Arrange — a row keyed by whatever the SELECT produced, with mixed value types.
        var expectedStream = Faker.Random.AlphaNumeric(12);
        var expectedCount  = Faker.Random.Int(1, 500);

        var response = new Contracts.QueryResponse { Truncated = true };

        response.Rows.Add(new Struct {
            Fields = {
                ["stream"] = Value.ForString(expectedStream),
                ["n"]      = Value.ForNumber(expectedCount),
            },
        });

        // Act
        var result = McpRecordMappers.ToModel(response);

        // Assert — the row keeps its own shape, and the truncation flag survives: an agent that misses
        // it reads an incomplete answer as a complete one.
        await Assert.That(result.Rows.Count).IsEqualTo(1);
        await Assert.That(result.Rows[0].GetProperty("stream").GetString()).IsEqualTo(expectedStream);
        await Assert.That(result.Rows[0].GetProperty("n").GetInt32()).IsEqualTo(expectedCount);
        await Assert.That(result.Truncated).IsTrue();
    }

    [Test]
    public async ValueTask folds_a_hit_into_the_model() {
        // Arrange
        var expectedScore     = Faker.Random.Double(0.1, 1.0);
        var expectedRecordId  = Faker.Random.Guid().ToString();
        var expectedStream    = Faker.Random.AlphaNumeric(12);
        var expectedCategory  = Faker.Random.AlphaNumeric(8);
        var expectedPosition  = Faker.Random.Long(1, long.MaxValue);
        var expectedData      = Faker.Lorem.Sentence();
        var expectedName      = Faker.Random.AlphaNumeric(10);
        var expectedSchemaId  = Faker.Random.Guid().ToString();
        var expectedCreatedAt = Faker.Date.RecentOffset().ToUniversalTime();
        var expectedTenant    = Faker.Company.CompanyName();

        var response = new Contracts.SearchResponse();

        response.Hits.Add(new Contracts.SearchResponse.Types.RecordHit {
            Score = expectedScore,
            Record = new() {
                Properties  = { ["tenant"] = Value.ForString(expectedTenant), ["retries"] = Value.ForNumber(3) },
                RecordId    = expectedRecordId,
                Stream      = expectedStream,
                Category    = expectedCategory,
                LogPosition = expectedPosition,
                Data        = expectedData,
                CreatedAt   = Timestamp.FromDateTimeOffset(expectedCreatedAt),
                Schema      = new() { Format = "Json", Name = expectedName, Id = expectedSchemaId },
            },
        });

        // Act
        var result = McpRecordMappers.ToModel(response);

        // Assert
        await Assert.That(result.Hits.Count).IsEqualTo(1);

        var hit = result.Hits[0];

        await Assert.That(hit.Score).IsEqualTo(expectedScore);
        await Assert.That(hit.Record.RecordId).IsEqualTo(expectedRecordId);
        await Assert.That(hit.Record.Stream).IsEqualTo(expectedStream);
        await Assert.That(hit.Record.Category).IsEqualTo(expectedCategory);
        await Assert.That(hit.Record.LogPosition).IsEqualTo(expectedPosition);
        await Assert.That(hit.Record.Data).IsEqualTo(expectedData);
        await Assert.That(hit.Record.CreatedAt).IsEqualTo(expectedCreatedAt);
        await Assert.That(hit.Record.Schema.Format).IsEqualTo("Json");
        await Assert.That(hit.Record.Schema.Name).IsEqualTo(expectedName);
        await Assert.That(hit.Record.Schema.Id).IsEqualTo(expectedSchemaId);
        // A property is any JSON value, not just a string — a number must not arrive quoted.
        await Assert.That(hit.Record.Properties["tenant"].GetString()).IsEqualTo(expectedTenant);
        await Assert.That(hit.Record.Properties["retries"].ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(hit.Record.Properties["retries"].GetInt32()).IsEqualTo(3);
    }
}
