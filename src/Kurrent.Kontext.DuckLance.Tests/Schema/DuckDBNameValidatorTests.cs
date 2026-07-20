using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Schema;

/// <summary>
/// Unit tests for <see cref="DuckDBNameValidator"/> table-name validation.
/// </summary>
public class DuckDBNameValidatorTests {
    [Test]
    [Arguments("docs")]
    [Arguments("store_collection")]
    [Arguments("c_2_3")]
    [Arguments("A1_b2")]
    [Arguments("docs_2024_01")]
    public async Task GetValidatedTableName_ValidInputs_ReturnsCollectionName(string collectionName) {
        var result = DuckDBNameValidator.GetValidatedTableName(collectionName);

        await Assert.That(result).IsEqualTo(collectionName);
    }

    [Test]
    [Arguments("")]          // empty collection name
    [Arguments("do cs")]     // space
    [Arguments("do'cs")]     // single quote
    [Arguments("do;cs")]     // semicolon
    [Arguments("do\"cs")]    // double quote
    [Arguments("do😀cs")]    // unicode emoji
    [Arguments("../x")]      // path traversal
    [Arguments("docs/sub")]  // path separator
    [Arguments("my.docs")]   // period: catalog separator in SQL identifiers and search-function URIs
    [Arguments("docs-2024")] // hyphen: parses as an operator in unquoted SQL identifiers
    public async Task GetValidatedTableName_InvalidInputs_ThrowsArgumentException(string collectionName) =>
        await Assert
            .That(() => { DuckDBNameValidator.GetValidatedTableName(collectionName); })
            .Throws<ArgumentException>();

    [Test]
    public async Task GetValidatedTableName_InvalidCollectionName_MessageNamesRule() =>
        await Assert
            .That(() => { DuckDBNameValidator.GetValidatedTableName("bad name"); })
            .Throws<ArgumentException>()
            .WithMessageContaining("collection name");
}
