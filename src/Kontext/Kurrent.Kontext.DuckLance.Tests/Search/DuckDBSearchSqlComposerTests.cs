using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Search;

/// <summary>
/// Pure unit tests for <see cref="DuckDBSearchSqlComposer"/>: the <c>lance_vector_search</c> statement composed
/// from the query parameters. No DuckDB connection is used; every test asserts the exact SQL text.
/// </summary>
public class DuckDBSearchSqlComposerTests {
    // The canonical non-vector projection for the golden model (key id, data category/tags/content).
    static readonly string[] s_canonicalColumns = ["id", "category", "tags", "content"];

    [Test]
    public async Task BuildVectorSearchSql_WithRefine_NoSkip_ProducesGoldenStatement() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            10, true, s_canonicalColumns,
            10, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 10, prefilter := true, refine_factor := 4)
                ORDER BY _distance LIMIT 10
                """);
    }

    [Test]
    public async Task BuildVectorSearchSql_WithoutRefine_NoSkip_OmitsRefineFactor() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            10, false, s_canonicalColumns,
            10, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 10, prefilter := true)
                ORDER BY _distance LIMIT 10
                """);
    }

    [Test]
    public async Task BuildVectorSearchSql_WithSkip_EmitsOffsetClause() {
        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            30, true, s_canonicalColumns,
            10, 20);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 30, prefilter := true, refine_factor := 4)
                ORDER BY _distance LIMIT 10 OFFSET 20
                """);
    }

    [Test]
    public async Task BuildVectorSearchSql_IncludeVectors_ProjectsVectorColumn() {
        string[] columns = ["id", "category", "tags", "content", "vec"];

        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            5, false, columns,
            5, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, vec, _distance
                FROM lance_vector_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 5, prefilter := true)
                ORDER BY _distance LIMIT 5
                """);
    }

    [Test]
    public async Task BuildVectorSearchSql_RespectsColumnOrder() {
        IReadOnlyList<string> columns = new List<string> {
            "gamma",
            "alpha",
            "beta"
        };

        var sql = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            "ns.main.t", "embedding", 8,
            3, false, columns,
            3, 0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT gamma, alpha, beta, _distance
                FROM lance_vector_search('ns.main.t', 'embedding', CAST(? AS FLOAT[8]), k := 3, prefilter := true)
                ORDER BY _distance LIMIT 3
                """);
    }

    [Test]
    public async Task BuildHybridSearchSql_NoWhere_WithRefine_NoSkip_ProducesGoldenStatement() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 10, true,
            s_canonicalColumns, null, 10,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 10, prefilter := true, alpha := 0.5, refine_factor := 4)
                ORDER BY _hybrid_score DESC LIMIT 10
                """);
    }

    [Test]
    public async Task BuildHybridSearchSql_NoWhere_WithoutRefine_OmitsRefineFactor() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 10, false,
            s_canonicalColumns, null, 10,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 10, prefilter := true, alpha := 0.5)
                ORDER BY _hybrid_score DESC LIMIT 10
                """);
    }

    [Test]
    public async Task BuildHybridSearchSql_WithWhere_WithRefine_WithSkip_EmitsWhereAndOffset() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 30, true,
            s_canonicalColumns, "category = ?", 10,
            20);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 30, prefilter := true, alpha := 0.5, refine_factor := 4)
                WHERE category = ? ORDER BY _hybrid_score DESC LIMIT 10 OFFSET 20
                """);
    }

    [Test]
    public async Task BuildHybridSearchSql_WithWhere_WithoutRefine_NoSkip_EmitsWhereOnly() {
        var sql = DuckDBSearchSqlComposer.BuildHybridSearchSql(
            "ducklance.main.vs_docs", "vec", 4,
            "content", 5, false,
            s_canonicalColumns, "array_has_any(tags, ?)", 5,
            0);

        await Assert
            .That(sql)
            .IsEqualTo(
                """
                SELECT id, category, tags, content, _hybrid_score, _distance, _score
                FROM lance_hybrid_search('ducklance.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), 'content', ?, k := 5, prefilter := true, alpha := 0.5)
                WHERE array_has_any(tags, ?) ORDER BY _hybrid_score DESC LIMIT 5
                """);
    }
}