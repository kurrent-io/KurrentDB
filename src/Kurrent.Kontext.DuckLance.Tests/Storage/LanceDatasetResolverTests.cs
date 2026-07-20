using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="LanceDatasetResolver"/> dataset path and table name resolution.
/// </summary>
public class LanceDatasetResolverTests {
    [Test]
    [Arguments("docs")]
    [Arguments("collection")]
    [Arguments("c_2_3")]
    [Arguments("A1_b2")]
    public async Task GetTableName_ValidInputs_ReturnsCollectionName(string collectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetTableName(collectionName);

        await Assert.That(result).IsEqualTo(collectionName);
    }

    [Test]
    [Arguments("docs")]
    [Arguments("collection")]
    [Arguments("c_2_3")]
    public async Task GetQualifiedTableName_ValidInputs_ReturnsExpectedQualifiedName(string collectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");
        var expected = $"{LanceDatasetResolver.DefaultStorageAlias}.main.{collectionName}";

        var result = resolver.GetQualifiedTableName(collectionName);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task GetQualifiedTableName_StandardCase_IncludesDefaultAlias() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetQualifiedTableName("docs");

        await Assert.That(result).StartsWith("ldb.main.");
        await Assert.That(result).IsEqualTo("ldb.main.docs");
    }

    [Test]
    public async Task GetDatasetPath_ValidInputs_ReturnsPathWithLanceExtension() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetDatasetPath("docs");

        await Assert.That(result).Contains("docs.lance");
    }

    [Test]
    public async Task GetDatasetPath_ValidInputs_PathStartsWithDatabaseDirectory() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetDatasetPath("docs");

        await Assert.That(result).StartsWith(resolver.DatabaseDirectory);
    }

    [Test]
    public async Task GetDatasetPath_ValidInputs_UsesPathCombine() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result          = resolver.GetDatasetPath("docs");
        var expectedSubpath = Path.Combine("docs.lance");
        var expected        = Path.Combine(resolver.DatabaseDirectory, expectedSubpath);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task DatabasePath_RelativePathProvided_ReturnsAbsolutePath() {
        var resolver = new LanceDatasetResolver(Path.Combine("some-rel-dir", "duck.db"));

        var result = resolver.DatabasePath;

        await Assert.That(Path.IsPathRooted(result)).IsTrue();
    }

    [Test]
    public async Task DatabasePath_AbsolutePathProvided_ReturnsResolvedPath() {
        var absolutePath = Path.GetFullPath("/tmp/test/duck.db");
        var resolver     = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.DatabasePath;

        await Assert.That(result).IsEqualTo(absolutePath);
    }

    [Test]
    public async Task DatabaseDirectory_IsTheDatabaseFilesDirectory() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.DatabaseDirectory;

        await Assert.That(result).IsEqualTo(Path.GetFullPath("/tmp/test"));
    }

    [Test]
    [Arguments(" ")]
    [Arguments("do cs")]
    [Arguments("do'cs")]
    [Arguments("do;cs")]
    [Arguments("my.docs")]
    [Arguments("my-docs")]
    public async Task GetTableName_InvalidCollectionNames_ThrowsArgumentException(string invalidCollectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetTableName(invalidCollectionName); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task GetQualifiedTableName_InvalidCollectionName_ThrowsArgumentException() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetQualifiedTableName("bad name"); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task GetDatasetPath_InvalidCollectionName_ThrowsArgumentException() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetDatasetPath("bad name"); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_EmptyDatabasePath_ThrowsArgumentException() =>
        await Assert
            .That(() => { new LanceDatasetResolver(""); })
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_ValidInputs_CreatesInstance() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert.That(resolver).IsNotNull();
        await Assert.That(resolver.DatabasePath).IsNotNull();
        await Assert.That(resolver.DatabaseDirectory).IsNotNull();
    }

    // [Test]
    // public async Task DefaultStorageAlias_IsConstant()
    // {
    //     await Assert.That(LanceDatasetResolver.DefaultStorageAlias).IsEqualTo("ldb");
    // }
}
