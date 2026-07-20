using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Schema;

/// <summary>
/// Unit tests for <see cref="DuckDBModelBuilder"/> schema validation: key/data/vector type support,
/// dimension and storage-name gating, and definition-driven (dynamic) builds.
/// </summary>
public class DuckDBModelBuilderTests {
    static CollectionModel Build<TRecord>(Type keyType) =>
        new DuckDBModelBuilder().Build(
            typeof(TRecord), keyType, null,
            null);

    // 1. Valid model builds.
    [Test]
    public async Task ValidRecord_Builds_WithExpectedPropertyShape() {
        var model = Build<ValidRecord>(typeof(string));

        await Assert.That(model.KeyProperties.Count).IsEqualTo(1);
        await Assert.That(model.KeyProperty.Type).IsEqualTo(typeof(string));
        await Assert.That(model.DataProperties.Count).IsEqualTo(3);
        await Assert.That(model.VectorProperties.Count).IsEqualTo(1);
        await Assert.That(model.VectorProperties[0].Dimensions).IsEqualTo(4);
    }

    // 2. Non-string key rejected.
    [Test]
    public async Task IntKey_Throws_NotSupported() =>
        await Assert
            .That(() => { Build<IntKeyRecord>(typeof(int)); })
            .Throws<NotSupportedException>()
            .WithMessageContaining("string");

    [Test]
    public async Task LongKey_Throws_NotSupported() =>
        await Assert
            .That(() => { Build<LongKeyRecord>(typeof(long)); })
            .Throws<NotSupportedException>()
            .WithMessageContaining("string");

    [Test]
    public async Task GuidKey_Throws_NotSupported() =>
        await Assert
            .That(() => { Build<GuidKeyRecord>(typeof(Guid)); })
            .Throws<NotSupportedException>()
            .WithMessageContaining("string");

    // 3. Bad vector CLR type rejected. The MEVD abstractions layer frames unsupported vector types as an
    // embedding-generation error ("... isn't supported by your provider ..."), so the message states the type
    // is not supported and names the offending type rather than enumerating the provider's supported vector types.
    [Test]
    public async Task ReadOnlyMemoryDoubleVector_Throws() =>
        await Assert
            .That(() => { Build<RomDoubleVectorRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("ReadOnlyMemory<double>");

    [Test]
    public async Task DoubleArrayVector_Throws() =>
        await Assert
            .That(() => { Build<DoubleArrayVectorRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("isn't supported");

    [Test]
    public async Task ListFloatVector_Throws() =>
        await Assert
            .That(() => { Build<ListFloatVectorRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("List<float>");

    // 4. Bad dimension rejected. Rejected by the MEVD abstractions layer (VectorStoreVectorProperty), which
    // throws ArgumentOutOfRangeException "Dimensions must be greater than zero" at Build time.
    [Test]
    public async Task ZeroDimensionVector_Throws_MentioningDimension() =>
        await Assert
            .That(() => { Build<ZeroDimRecord>(typeof(string)); })
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining("Dimensions");

    [Test]
    public async Task NegativeDimensionVector_Throws_MentioningDimension() =>
        await Assert
            .That(() => { Build<NegativeDimRecord>(typeof(string)); })
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining("Dimensions");

    // 5. Unsupported data type rejected, naming supported types (message embeds DuckDBModelBuilder.SupportedDataTypes).
    [Test]
    public async Task TimeSpanData_Throws_NamingSupportedTypes() =>
        await Assert
            .That(() => { Build<TimeSpanDataRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("DateTimeOffset");

    [Test]
    public async Task GuidData_Throws_NamingSupportedTypes() =>
        await Assert
            .That(() => { Build<GuidDataRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("DateTimeOffset");

    [Test]
    public async Task NestedPocoData_Throws_NamingSupportedTypes() =>
        await Assert
            .That(() => { Build<NestedPocoDataRecord>(typeof(string)); })
            .Throws<Exception>()
            .WithMessageContaining("DateTimeOffset");

    // 6. StorageName override honored.
    [Test]
    public async Task StorageNameOverride_IsHonored() {
        var model = Build<StorageNameOverrideRecord>(typeof(string));

        var category = model.DataProperties.Single(p => p.ModelName == "Category");
        await Assert.That(category.StorageName).IsEqualTo("cat_col");
    }

    // 6a. Case normalization: default (PascalCase-derived) storage names are lowercased. Regression test for
    // the DuckDB lance extension's broken DELETE predicate pushdown on mixed-case column names (validated
    // against extension build 533e0ee): DELETE ... WHERE Id = ? silently matches zero rows, while lowercase
    // `id` works. DuckDBModelBuilder.Customize() unconditionally lowercases every StorageName to sidestep this.
    [Test]
    public async Task DefaultStorageNames_AreLowercased() {
        var model = Build<ValidRecord>(typeof(string));

        await Assert.That(model.KeyProperty.StorageName).IsEqualTo("id");

        var category = model.DataProperties.Single(p => p.ModelName == "Category");
        await Assert.That(category.StorageName).IsEqualTo("category");

        var tags = model.DataProperties.Single(p => p.ModelName == "Tags");
        await Assert.That(tags.StorageName).IsEqualTo("tags");

        var content = model.DataProperties.Single(p => p.ModelName == "Content");
        await Assert.That(content.StorageName).IsEqualTo("content");

        await Assert.That(model.VectorProperties[0].StorageName).IsEqualTo("vec");
    }

    // 6b. Case normalization: an explicit mixed-case StorageName override is also lowercased.
    [Test]
    public async Task ExplicitMixedCaseStorageName_IsNormalizedToLowercase() {
        var model = Build<MixedCaseStorageNameRecord>(typeof(string));

        var category = model.DataProperties.Single(p => p.ModelName == "Category");
        await Assert.That(category.StorageName).IsEqualTo("cat_col");
    }

    // 6c. Case-only storage-name collisions are rejected, naming both colliding model properties.
    [Test]
    public async Task CaseOnlyStorageNameCollision_Throws_ArgumentException_NamingBothProperties() =>
        await Assert
            .That(() => { Build<CaseOnlyCollisionRecord>(typeof(string)); })
            .Throws<ArgumentException>()
            .WithMessageContaining("Value")
            .And
            .WithMessageContaining("Other");

    // 7. Malicious StorageName rejected by our storage-name gate.
    [Test]
    public async Task StorageNameWithSpace_Throws_ArgumentException() =>
        await Assert
            .That(() => { Build<SpaceStorageNameRecord>(typeof(string)); })
            .Throws<ArgumentException>();

    [Test]
    public async Task StorageNameWithSqlInjection_Throws_ArgumentException() =>
        await Assert
            .That(() => { Build<DropTableStorageNameRecord>(typeof(string)); })
            .Throws<ArgumentException>();

    [Test]
    public async Task StorageNameWithQuote_Throws_ArgumentException() =>
        await Assert
            .That(() => { Build<QuoteStorageNameRecord>(typeof(string)); })
            .Throws<ArgumentException>();

    // 8. Definition-as-source-of-truth (dynamic build).
    [Test]
    public async Task BuildDynamic_ValidDefinition_Builds() {
        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)),
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        var model = new DuckDBModelBuilder().BuildDynamic(definition, null);

        await Assert.That(model.KeyProperty.Type).IsEqualTo(typeof(string));
        await Assert.That(model.DataProperties.Count).IsEqualTo(1);
        await Assert.That(model.VectorProperties.Count).IsEqualTo(1);
        await Assert.That(model.VectorProperties[0].Dimensions).IsEqualTo(4);
    }

    [Test]
    public async Task BuildDynamic_IntKeyDefinition_Throws() {
        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(int)),
                new VectorStoreDataProperty("Category", typeof(string)),
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        await Assert
            .That(() => { new DuckDBModelBuilder().BuildDynamic(definition, null); })
            .Throws<NotSupportedException>();
    }

    // 9. Multiple vector properties accepted (SupportsMultipleVectors = true).
    [Test]
    public async Task MultipleVectors_Build_WithDistinctDimensions() {
        var model = Build<MultiVectorRecord>(typeof(string));

        await Assert.That(model.VectorProperties.Count).IsEqualTo(2);

        var dims = model.VectorProperties.Select(v => v.Dimensions).OrderBy(d => d).ToList();
        await Assert.That(dims[0]).IsEqualTo(3);
        await Assert.That(dims[1]).IsEqualTo(8);
    }

    #region Test records

    sealed class ValidRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public string Category { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public List<string> Tags { get; set; } = [];

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class IntKeyRecord {
        [VectorStoreKey] public int Id { get; set; }

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class LongKeyRecord {
        [VectorStoreKey] public long Id { get; set; }

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class GuidKeyRecord {
        [VectorStoreKey] public Guid Id { get; set; }

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class RomDoubleVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<double> Vec { get; set; }
    }

    sealed class DoubleArrayVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public double[] Vec { get; set; } = [];
    }

    sealed class ListFloatVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(4)] public List<float> Vec { get; set; } = [];
    }

    sealed class ZeroDimRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(0)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class NegativeDimRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(-1)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class TimeSpanDataRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public TimeSpan Duration { get; set; }

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class GuidDataRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public Guid Reference { get; set; }

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class NestedPoco {
        public string Value { get; set; } = "";
    }

    sealed class NestedPocoDataRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public NestedPoco Nested { get; set; } = new();

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class StorageNameOverrideRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "cat_col")]
        public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class MixedCaseStorageNameRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "Cat_Col")]
        public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class CaseOnlyCollisionRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Value { get; set; } = "";

        [VectorStoreData(StorageName = "VALUE")]
        public string Other { get; set; } = "";
    }

    sealed class SpaceStorageNameRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "bad name")]
        public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class DropTableStorageNameRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "x; DROP TABLE")]
        public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class QuoteStorageNameRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "x\"")] public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class MultiVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreVector(3)] public ReadOnlyMemory<float> VecA { get; set; }

        [VectorStoreVector(8)] public ReadOnlyMemory<float> VecB { get; set; }
    }

    #endregion
}