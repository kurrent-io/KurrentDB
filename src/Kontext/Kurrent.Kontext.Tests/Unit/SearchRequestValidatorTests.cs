// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Records;
using KurrentDB.Testing.Bogus;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// The records search contract's mechanically-checkable rules. `scope` and `schema` are protobuf oneofs,
/// so their exclusivity is structural and belongs to the mapper that collapses four MCP members into
/// them — see <see cref="McpRecordMappersTests"/>.
/// </summary>
public class SearchRequestValidatorTests {
    static readonly SearchRequestValidator Validator = new();

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async ValueTask accepts_a_query_on_its_own() {
        // Arrange
        var request = new Contracts.SearchRequest { Query = Faker.Lorem.Sentence() };

        // Act
        var result = Validator.Validate(request);

        // Assert — the whole-log search, with the server picking limit and cutoff.
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask rejects_an_empty_query() {
        // Arrange
        var request = new Contracts.SearchRequest { Limit = Faker.Random.Int(1, 50) };

        // Act
        var result = Validator.Validate(request);

        // Assert — there is nothing to embed, so the request cannot be answered at all.
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask rejects_a_negative_limit() {
        // Arrange
        var request = new Contracts.SearchRequest {
            Query = Faker.Lorem.Sentence(),
            Limit = Faker.Random.Int(-50, -1),
        };

        // Act
        var result = Validator.Validate(request);

        // Assert — 0 means "server default", so a negative value is not a smaller default, it is nonsense.
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask query_accepts_sql_on_its_own() {
        // Arrange
        var request = new Contracts.QueryRequest { Sql = "SELECT count(*) FROM kdb.records" };

        // Act
        var result = new QueryRequestValidator().Validate(request);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask query_rejects_empty_sql() {
        // Arrange
        var request = new Contracts.QueryRequest { Limit = Faker.Random.Int(1, 50) };

        // Act
        var result = new QueryRequestValidator().Validate(request);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask query_accepts_sql_naming_a_forbidden_table() {
        // Arrange — the query engine parses the statement and rejects what it may not touch. Deciding
        // that here too would be a second, drifting copy of its allowlist.
        var request = new Contracts.QueryRequest { Sql = "SELECT * FROM pg_catalog.pg_tables" };

        // Act
        var result = new QueryRequestValidator().Validate(request);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask rejects_a_negative_minimum_score() {
        // Arrange
        var request = new Contracts.SearchRequest {
            Query    = Faker.Lorem.Sentence(),
            MinScore = Faker.Random.Double(-5, -0.1),
        };

        // Act
        var result = Validator.Validate(request);

        // Assert — scores are non-negative, so a negative cutoff admits everything while reading as a filter.
        await Assert.That(result.IsValid).IsFalse();
    }
}
