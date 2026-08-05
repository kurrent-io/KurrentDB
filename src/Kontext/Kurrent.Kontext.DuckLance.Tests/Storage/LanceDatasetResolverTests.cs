using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="LanceDatasetResolver"/> dataset path and table name resolution.
/// </summary>
[Category("Storage")]
public class LanceDatasetResolverTests {
    [Test]
    [Arguments("docs")]
    [Arguments("collection")]
    [Arguments("c_2_3")]
    [Arguments("A1_b2")]
    public async ValueTask get_table_name_valid_inputs_returns_collection_name(string collectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetTableName(collectionName);

        await Assert.That(result).IsEqualTo(collectionName);
    }

    [Test]
    [Arguments("docs")]
    [Arguments("collection")]
    [Arguments("c_2_3")]
    public async ValueTask get_qualified_table_name_valid_inputs_returns_expected_qualified_name(string collectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");
        var expected = $"{LanceDatasetResolver.DefaultStorageAlias}.main.{collectionName}";

        var result = resolver.GetQualifiedTableName(collectionName);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask get_qualified_table_name_standard_case_includes_default_alias() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetQualifiedTableName("docs");

        await Assert.That(result).StartsWith("ldb.main.");
        await Assert.That(result).IsEqualTo("ldb.main.docs");
    }

    [Test]
    public async ValueTask get_dataset_path_valid_inputs_returns_path_with_lance_extension() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetDatasetPath("docs");

        await Assert.That(result).Contains("docs.lance");
    }

    [Test]
    public async ValueTask get_dataset_path_valid_inputs_path_starts_with_database_directory() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.GetDatasetPath("docs");

        await Assert.That(result).StartsWith(resolver.DatabaseDirectory);
    }

    [Test]
    public async ValueTask get_dataset_path_valid_inputs_uses_path_combine() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        var result          = resolver.GetDatasetPath("docs");
        var expectedSubpath = Path.Combine("docs.lance");
        var expected        = Path.Combine(resolver.DatabaseDirectory, expectedSubpath);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask database_path_relative_path_provided_returns_absolute_path() {
        var resolver = new LanceDatasetResolver(Path.Combine("some-rel-dir", "duck.db"));

        var result = resolver.DatabasePath;

        await Assert.That(Path.IsPathRooted(result)).IsTrue();
    }

    [Test]
    public async ValueTask database_path_absolute_path_provided_returns_resolved_path() {
        var absolutePath = Path.GetFullPath("/tmp/test/duck.db");
        var resolver     = new LanceDatasetResolver("/tmp/test/duck.db");

        var result = resolver.DatabasePath;

        await Assert.That(result).IsEqualTo(absolutePath);
    }

    [Test]
    public async ValueTask database_directory_is_the_database_files_directory() {
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
    public async ValueTask get_table_name_invalid_collection_names_throws_argument_exception(string invalidCollectionName) {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetTableName(invalidCollectionName); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask get_qualified_table_name_invalid_collection_name_throws_argument_exception() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetQualifiedTableName("bad name"); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask get_dataset_path_invalid_collection_name_throws_argument_exception() {
        var resolver = new LanceDatasetResolver("/tmp/test/duck.db");

        await Assert
            .That(() => { resolver.GetDatasetPath("bad name"); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask constructor_empty_database_path_throws_argument_exception() =>
        await Assert
            .That(() => { new LanceDatasetResolver(""); })
            .Throws<ArgumentException>();

    [Test]
    public async ValueTask constructor_valid_inputs_creates_instance() {
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
