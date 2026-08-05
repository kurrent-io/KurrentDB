using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Search;

/// <summary>
/// Pure unit tests for <see cref="DuckDBScoreConverter"/>: the raw <c>_distance</c> to score mapping and the
/// distance-function validation gate. No DuckDB connection is used; the oracle numbers are the frozen
/// Decision #1 mapping.
/// </summary>
[Category("Search")]
public class DuckDBScoreConverterTests {
    // Cosine distance (and the null default, which the connector treats as cosine): raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(1.0, 1.0)]
    [Arguments(2.0, 2.0)]
    public async ValueTask convert_score_cosine_distance_returns_raw_unchanged(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.CosineDistance, raw)).IsEqualTo(expected);

    // A null distance function defaults to cosine distance: raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(1.0, 1.0)]
    [Arguments(2.0, 2.0)]
    public async ValueTask convert_score_null_default_is_treated_as_cosine_distance(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(null, raw)).IsEqualTo(expected);

    // Cosine similarity: 1 - _distance, so 0/1/2 map to 1/0/-1.
    [Test]
    [Arguments(0.0, 1.0)]
    [Arguments(1.0, 0.0)]
    [Arguments(2.0, -1.0)]
    public async ValueTask convert_score_cosine_similarity_is_one_minus_distance(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.CosineSimilarity, raw)).IsEqualTo(expected);

    // Squared Euclidean distance: raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(2.0, 2.0)]
    [Arguments(4.0, 4.0)]
    public async ValueTask convert_score_euclidean_squared_distance_returns_raw_unchanged(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.EuclideanSquaredDistance, raw)).IsEqualTo(expected);

    // Dot-product similarity: 1 - _distance.
    [Test]
    public async ValueTask convert_score_dot_product_similarity_is_one_minus_distance() =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.DotProductSimilarity, 0.25)).IsEqualTo(0.75);

    // The five supported distance functions (plus the null default) validate without throwing.
    [Test]
    [Arguments(null)]
    [Arguments(DistanceFunction.CosineDistance)]
    [Arguments(DistanceFunction.CosineSimilarity)]
    [Arguments(DistanceFunction.DotProductSimilarity)]
    [Arguments(DistanceFunction.EuclideanSquaredDistance)]
    public async ValueTask validate_distance_function_supported_does_not_throw(string? distanceFunction) =>
        await Assert.That(() => DuckDBScoreConverter.ValidateDistanceFunction(distanceFunction)).ThrowsNothing();

    // Euclidean (non-squared) distance is a documented v1 limitation and is rejected up front.
    [Test]
    public async ValueTask validate_distance_function_euclidean_distance_throws() =>
        await Assert
            .That(() => DuckDBScoreConverter.ValidateDistanceFunction(DistanceFunction.EuclideanDistance))
            .Throws<NotSupportedException>()
            .WithMessageContaining(DistanceFunction.EuclideanDistance);

    // Any unrecognized distance function string is rejected.
    [Test]
    public async ValueTask validate_distance_function_unknown_string_throws() =>
        await Assert
            .That(() => DuckDBScoreConverter.ValidateDistanceFunction("NotARealDistanceFunction"))
            .Throws<NotSupportedException>();
}