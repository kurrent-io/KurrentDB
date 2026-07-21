using System.Diagnostics.CodeAnalysis;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Schema;

/// <summary>
/// Tests for <see cref="DuckDBSchemaBuilder"/>: the mapping from <see cref="CollectionModel"/> properties
/// to DuckDB column types, and the <c>CREATE TABLE IF NOT EXISTS</c> statement assembled from them.
/// </summary>
public class DuckDBSchemaBuilderTests {
    [Test]
    public async Task BuildCreateTableSql_Produces_The_Golden_Oracle_Statement() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("category", typeof(string)),
            new VectorStoreDataProperty("tags", typeof(List<string>)),
            new VectorStoreDataProperty("content", typeof(string)),
            new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 4));

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("ns.main.vs_docs", model);

        await Assert.That(sql).IsEqualTo("CREATE TABLE IF NOT EXISTS ns.main.vs_docs (id VARCHAR, category VARCHAR, tags VARCHAR[], content VARCHAR, vec FLOAT[4])");
    }

    [Test]
    public async Task GetDuckDbType_Maps_Every_Scalar_Type() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("sVal", typeof(string)),
            new VectorStoreDataProperty("bVal", typeof(bool)),
            new VectorStoreDataProperty("shVal", typeof(short)),
            new VectorStoreDataProperty("iVal", typeof(int)),
            new VectorStoreDataProperty("lVal", typeof(long)),
            new VectorStoreDataProperty("fVal", typeof(float)),
            new VectorStoreDataProperty("dVal", typeof(double)),
            new VectorStoreDataProperty("decVal", typeof(decimal)),
            new VectorStoreDataProperty("dtVal", typeof(DateTime)),
            new VectorStoreDataProperty("dtoVal", typeof(DateTimeOffset)),
            new VectorStoreDataProperty("blobVal", typeof(byte[])));

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("scalars", model);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE TABLE IF NOT EXISTS scalars (id VARCHAR, sVal VARCHAR, bVal BOOLEAN, shVal SMALLINT, " +
                "iVal INTEGER, lVal BIGINT, fVal FLOAT, dVal DOUBLE, decVal DECIMAL(38,18), dtVal TIMESTAMP, " +
                "dtoVal TIMESTAMP WITH TIME ZONE, blobVal BLOB)");
    }

    [Test]
    public async Task GetDuckDbType_Maps_Every_Array_Type() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("sArr", typeof(string[])),
            new VectorStoreDataProperty("bArr", typeof(bool[])),
            new VectorStoreDataProperty("shArr", typeof(short[])),
            new VectorStoreDataProperty("iArr", typeof(int[])),
            new VectorStoreDataProperty("lArr", typeof(long[])),
            new VectorStoreDataProperty("fArr", typeof(float[])),
            new VectorStoreDataProperty("dArr", typeof(double[])));

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("arrays", model);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE TABLE IF NOT EXISTS arrays (id VARCHAR, sArr VARCHAR[], bArr BOOLEAN[], shArr SMALLINT[], " +
                "iArr INTEGER[], lArr BIGINT[], fArr FLOAT[], dArr DOUBLE[])");
    }

    [Test]
    public async Task GetDuckDbType_Maps_Every_List_Type() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("sList", typeof(List<string>)),
            new VectorStoreDataProperty("bList", typeof(List<bool>)),
            new VectorStoreDataProperty("shList", typeof(List<short>)),
            new VectorStoreDataProperty("iList", typeof(List<int>)),
            new VectorStoreDataProperty("lList", typeof(List<long>)),
            new VectorStoreDataProperty("fList", typeof(List<float>)),
            new VectorStoreDataProperty("dList", typeof(List<double>)));

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("lists", model);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE TABLE IF NOT EXISTS lists (id VARCHAR, sList VARCHAR[], bList BOOLEAN[], shList SMALLINT[], " +
                "iList INTEGER[], lList BIGINT[], fList FLOAT[], dList DOUBLE[])");
    }

    [Test]
    public async Task GetDuckDbType_Unwraps_Nullable_Value_Types() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("count", typeof(int?)));

        await Assert.That(DuckDBSchemaBuilder.GetDuckDbType(model.DataProperties[0])).IsEqualTo("INTEGER");
    }

    [Test]
    public async Task BuildCreateTableSql_Respects_StorageName_Override() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("category", typeof(string)) { StorageName = "cat_override" });

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("storage_names", model);

        await Assert.That(sql).IsEqualTo("CREATE TABLE IF NOT EXISTS storage_names (id VARCHAR, cat_override VARCHAR)");
    }

    [Test]
    public async Task GetDuckDbType_Interpolates_Vector_Dimensions() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreVectorProperty("embedding", typeof(ReadOnlyMemory<float>), 768));

        await Assert.That(DuckDBSchemaBuilder.GetDuckDbType(model.VectorProperties[0])).IsEqualTo("FLOAT[768]");
    }

    [Test]
    public async Task BuildCreateTableSql_Supports_Multiple_Vector_Properties() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreVectorProperty("titleEmbedding", typeof(ReadOnlyMemory<float>), 384),
            new VectorStoreVectorProperty("bodyEmbedding", typeof(ReadOnlyMemory<float>), 1536));

        var sql = DuckDBSchemaBuilder.BuildCreateTableSql("multi_vec", model);

        await Assert.That(sql).IsEqualTo("CREATE TABLE IF NOT EXISTS multi_vec (id VARCHAR, titleEmbedding FLOAT[384], bodyEmbedding FLOAT[1536])");
    }

    [Test]
    public async Task GetDuckDbType_Maps_Decimal_Exactly() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("price", typeof(decimal)));

        await Assert.That(DuckDBSchemaBuilder.GetDuckDbType(model.DataProperties[0])).IsEqualTo("DECIMAL(38,18)");
    }

    [Test]
    public async Task GetDuckDbType_Maps_DateTimeOffset_Exactly() {
        var model = BuildModel(
            new VectorStoreKeyProperty("id", typeof(string)),
            new VectorStoreDataProperty("createdAt", typeof(DateTimeOffset)));

        await Assert.That(DuckDBSchemaBuilder.GetDuckDbType(model.DataProperties[0])).IsEqualTo("TIMESTAMP WITH TIME ZONE");
    }

    /// <summary>
    /// Builds a <see cref="CollectionModel"/> for dynamic mapping scenarios from the given properties, using a
    /// permissive model builder that accepts any CLR type. This lets these tests construct models without
    /// depending on the real (production) DuckLance model builder.
    /// </summary>
    static CollectionModel BuildModel(params VectorStoreProperty[] properties) {
        var definition = new VectorStoreCollectionDefinition { Properties = properties };

        return new PermissiveModelBuilder().BuildDynamic(definition, null);
    }

    /// <summary>
    /// A <see cref="CollectionModelBuilder"/> that accepts any CLR type for data and vector properties, and does
    /// not require at least one vector property. Used so these tests can build <see cref="CollectionModel"/>
    /// instances without depending on the real DuckLance model builder.
    /// </summary>
    sealed class PermissiveModelBuilder : CollectionModelBuilder {
        public PermissiveModelBuilder()
            : base(new() { RequiresAtLeastOneVector = false, SupportsMultipleVectors = true }) { }

        protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }

        protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }
    }
}