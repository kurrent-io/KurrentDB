using System.Linq.Expressions;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Filtering;

/// <summary>
/// Pure unit tests for <see cref="DuckDBFilterTranslator"/>: the LINQ-to-DuckDB <c>WHERE</c> translation. No
/// DuckDB connection is used — every test asserts the exact clause text, its ordered parameters, and its
/// oversample flag, or that an unsupported construct throws a <see cref="NotSupportedException"/> naming it.
/// </summary>
/// <remarks>
/// The canonical translator POCO is built by the real <see cref="DuckDBModelBuilder"/>: key <c>id</c>, an
/// IsIndexed string <c>category</c>, an IsIndexed <c>List&lt;string&gt;</c> <c>tags</c>, a non-indexed string
/// <c>content</c>, a vector <c>vec</c> (dim 4). Two extra properties exist only so the throw-cases can be written
/// as compilable expressions: an IsIndexed <c>bool flag</c> (for the boolean-shortcut case) and a non-indexed
/// <c>DateTime created</c> (for the DateTime-comparison case). The vector is modeled as <c>float[]</c> so
/// <c>r.Vec == array</c> is expressible (ReadOnlyMemory has no <c>==</c>); the translator treats every vector
/// property identically regardless of CLR shape.
/// </remarks>
public class DuckDBFilterTranslatorTests {
    static CollectionModel Model() =>
        new DuckDBModelBuilder().Build(
            typeof(FilterRecord), typeof(string), null,
            null);

    static DuckDBFilterResult Translate(Expression<Func<FilterRecord, bool>> filter) => new DuckDBFilterTranslator().Translate(filter, Model());

    // ---- Supported: equality ----

    [Test]
    public async Task Equality_Literal_EmitsColumnEqualsPlaceholder() {
        var result = Translate(r => r.Category == "cat_a");

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async Task Equality_ReversedOperandOrder_EmitsSameClause() {
        var result = Translate(r => "cat_a" == r.Category);

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async Task Equality_CapturedVariable_EmitsPlaceholderWithCapturedValue() {
        var captured = "cat_a";
        var result   = Translate(r => r.Category == captured);

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async Task Equality_OnKeyProperty_IsAllowed() {
        var result = Translate(r => r.Id == "k1");

        await Assert.That(result.WhereClause).IsEqualTo("id = ?");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters[0]).IsEqualTo("k1");
    }

    // ---- Supported: containment ----

    [Test]
    public async Task Containment_OnIndexedTagList_EmitsArrayHasAnyAndRequiresOversample() {
        var result = Translate(r => r.Tags.Contains("post"));

        await Assert.That(result.WhereClause).IsEqualTo("array_has_any(tags, [?])");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("post");
    }

    // ---- Supported: AndAlso composition ----

    [Test]
    public async Task AndAlso_TwoEqualities_EmitsParenthesizedAnd_NoOversample() {
        var result = Translate(r => r.Category == "c" && r.Id == "k");

        await Assert.That(result.WhereClause).IsEqualTo("(category = ? AND id = ?)");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters.Count).IsEqualTo(2);
        await Assert.That(result.Parameters[0]).IsEqualTo("c");
        await Assert.That(result.Parameters[1]).IsEqualTo("k");
    }

    [Test]
    public async Task AndAlso_EqualityAndContainment_RequiresOversample() {
        var result = Translate(r => r.Category == "c" && r.Tags.Contains("post"));

        await Assert.That(result.WhereClause).IsEqualTo("(category = ? AND array_has_any(tags, [?]))");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(2);
        await Assert.That(result.Parameters[0]).IsEqualTo("c");
        await Assert.That(result.Parameters[1]).IsEqualTo("post");
    }

    [Test]
    public async Task AndAlso_NestedComposition_TranslatesRecursively() {
        var result = Translate(r => r.Category == "c" && r.Id == "k" && r.Tags.Contains("t"));

        await Assert.That(result.WhereClause).IsEqualTo("((category = ? AND id = ?) AND array_has_any(tags, [?]))");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(3);
        await Assert.That(result.Parameters[0]).IsEqualTo("c");
        await Assert.That(result.Parameters[1]).IsEqualTo("k");
        await Assert.That(result.Parameters[2]).IsEqualTo("t");
    }

    // ---- Unsupported: each must throw NotSupportedException naming the construct ----

    [Test]
    public async Task Filter_OnNonIndexedProperty_Throws() =>
        await Assert
            .That(() => Translate(r => r.Content == "x"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("IsIndexed");

    [Test]
    public async Task Filter_BooleanPropertyShortcut_Throws() =>
        await Assert
            .That(() => Translate(r => r.Flag))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Flag");

    [Test]
    public async Task Filter_LogicalOr_Throws() =>
        await Assert
            .That(() => Translate(r => r.Category == "a" || r.Category == "b"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("||");

    [Test]
    public async Task Filter_Inequality_Throws() =>
        await Assert
            .That(() => Translate(r => r.Category != "a"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("!=");

    [Test]
    public async Task Filter_GreaterThan_Throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Length > 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("greater-than");

    [Test]
    public async Task Filter_LessThan_Throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Length < 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("less-than");

    [Test]
    public async Task Filter_LogicalNot_Throws() =>
        await Assert
            .That(() => Translate(r => !(r.Category == "a")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("NOT");

    [Test]
    public async Task Filter_ConstantPredicate_Throws() =>
        await Assert
            .That(() => Translate(r => true))
            .Throws<NotSupportedException>()
            .WithMessageContaining("constant");

    [Test]
    public async Task Filter_NestedMemberAccess_Throws() =>
        await Assert
            .That(() => Translate(r => r.Category.Length == 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Length");

    [Test]
    public async Task Filter_PropertyToProperty_Throws() =>
        await Assert
            .That(() => Translate(r => r.Category == r.Content))
            .Throws<NotSupportedException>()
            .WithMessageContaining("property-to-property");

    [Test]
    public async Task Filter_StringContains_Throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Contains("x")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Contains");

    [Test]
    public async Task Filter_InlineArrayContains_Throws() =>
        await Assert
            .That(() => Translate(r => new[] { "a" }.Contains(r.Category)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("IN-style");

    [Test]
    public async Task Filter_ContainsNull_Throws() =>
        await Assert
            .That(() => Translate(r => r.Tags.Contains(null!)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("non-null string");

    [Test]
    public async Task Filter_EqualityWithNull_Throws() =>
        await Assert
            .That(() => Translate(r => r.Category == null))
            .Throws<NotSupportedException>()
            .WithMessageContaining("null");

    [Test]
    public async Task Filter_OnVectorProperty_Throws() {
        float[] probe = [1f, 0f, 0f, 0f];

        await Assert
            .That(() => Translate(r => r.Vec == probe))
            .Throws<NotSupportedException>()
            .WithMessageContaining("vector");
    }

    [Test]
    public async Task Filter_EnumerableAny_Throws() =>
        await Assert
            .That(() => Translate(r => r.Tags.Any(t => t == "x")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Any");

    [Test]
    public async Task Filter_DateTimeComparisonOnNonIndexedProperty_Throws() =>
        await Assert
            .That(() => Translate(r => r.Created == new DateTime(2020, 1, 1)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("IsIndexed");

    /// <summary>
    /// The canonical translator POCO. See the class remarks for why <c>Flag</c>, <c>Created</c>, and a
    /// <c>float[]</c> vector are present.
    /// </summary>
    sealed class FilterRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "";

        [VectorStoreData(StorageName = "flag", IsIndexed = true)]
        public bool Flag { get; set; }

        [VectorStoreData(StorageName = "created")]
        public DateTime Created { get; set; }

        [VectorStoreVector(4, StorageName = "vec")]
        public float[] Vec { get; set; } = [];
    }
}