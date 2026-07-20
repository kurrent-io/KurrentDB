using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Search;

/// <summary>
/// Pure unit tests for <see cref="DuckDBScoreConverter"/>: the raw <c>_distance</c> to score mapping and the
/// distance-function validation gate. No DuckDB connection is used; the oracle numbers are the frozen
/// Decision #1 mapping.
/// </summary>
public class DuckDBScoreConverterTests {
    // Cosine distance (and the null default, which the connector treats as cosine): raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(1.0, 1.0)]
    [Arguments(2.0, 2.0)]
    public async Task ConvertScore_CosineDistance_ReturnsRawUnchanged(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.CosineDistance, raw)).IsEqualTo(expected);

    // A null distance function defaults to cosine distance: raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(1.0, 1.0)]
    [Arguments(2.0, 2.0)]
    public async Task ConvertScore_NullDefault_IsTreatedAsCosineDistance(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(null, raw)).IsEqualTo(expected);

    // Cosine similarity: 1 - _distance, so 0/1/2 map to 1/0/-1.
    [Test]
    [Arguments(0.0, 1.0)]
    [Arguments(1.0, 0.0)]
    [Arguments(2.0, -1.0)]
    public async Task ConvertScore_CosineSimilarity_IsOneMinusDistance(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.CosineSimilarity, raw)).IsEqualTo(expected);

    // Squared Euclidean distance: raw _distance unchanged.
    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(2.0, 2.0)]
    [Arguments(4.0, 4.0)]
    public async Task ConvertScore_EuclideanSquaredDistance_ReturnsRawUnchanged(double raw, double expected) =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.EuclideanSquaredDistance, raw)).IsEqualTo(expected);

    // Dot-product similarity: 1 - _distance.
    [Test]
    public async Task ConvertScore_DotProductSimilarity_IsOneMinusDistance() =>
        await Assert.That(DuckDBScoreConverter.ConvertScore(DistanceFunction.DotProductSimilarity, 0.25)).IsEqualTo(0.75);

    // The five supported distance functions (plus the null default) validate without throwing.
    [Test]
    [Arguments(null)]
    [Arguments(DistanceFunction.CosineDistance)]
    [Arguments(DistanceFunction.CosineSimilarity)]
    [Arguments(DistanceFunction.DotProductSimilarity)]
    [Arguments(DistanceFunction.EuclideanSquaredDistance)]
    public async Task ValidateDistanceFunction_Supported_DoesNotThrow(string? distanceFunction) =>
        await Assert.That(() => DuckDBScoreConverter.ValidateDistanceFunction(distanceFunction)).ThrowsNothing();

    // Euclidean (non-squared) distance is a documented v1 limitation and is rejected up front.
    [Test]
    public async Task ValidateDistanceFunction_EuclideanDistance_Throws() =>
        await Assert
            .That(() => DuckDBScoreConverter.ValidateDistanceFunction(DistanceFunction.EuclideanDistance))
            .Throws<NotSupportedException>()
            .WithMessageContaining(DistanceFunction.EuclideanDistance);

    // Any unrecognized distance function string is rejected.
    [Test]
    public async Task ValidateDistanceFunction_UnknownString_Throws() =>
        await Assert
            .That(() => DuckDBScoreConverter.ValidateDistanceFunction("NotARealDistanceFunction"))
            .Throws<NotSupportedException>();
}