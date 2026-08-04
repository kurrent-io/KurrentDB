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
[Category("Filtering")]
public class DuckDBFilterTranslatorTests {
    static CollectionModel Model() =>
        new DuckDBModelBuilder().Build(
            typeof(FilterRecord), typeof(string), null,
            null);

    static DuckDBFilterResult Translate(Expression<Func<FilterRecord, bool>> filter) => new DuckDBFilterTranslator().Translate(filter, Model());

    // ---- Supported: equality ----

    [Test]
    public async ValueTask equality_literal_emits_column_equals_placeholder() {
        var result = Translate(r => r.Category == "cat_a");

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async ValueTask equality_reversed_operand_order_emits_same_clause() {
        var result = Translate(r => "cat_a" == r.Category);

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async ValueTask equality_captured_variable_emits_placeholder_with_captured_value() {
        var captured = "cat_a";
        var result   = Translate(r => r.Category == captured);

        await Assert.That(result.WhereClause).IsEqualTo("category = ?");
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("cat_a");
    }

    [Test]
    public async ValueTask equality_on_key_property_is_allowed() {
        var result = Translate(r => r.Id == "k1");

        await Assert.That(result.WhereClause).IsEqualTo("id = ?");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters[0]).IsEqualTo("k1");
    }

    // ---- Supported: containment ----

    [Test]
    public async ValueTask containment_on_indexed_tag_list_emits_array_has_any_and_requires_oversample() {
        var result = Translate(r => r.Tags.Contains("post"));

        await Assert.That(result.WhereClause).IsEqualTo("array_has_any(tags, [?])");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(1);
        await Assert.That(result.Parameters[0]).IsEqualTo("post");
    }

    // ---- Supported: AndAlso composition ----

    [Test]
    public async ValueTask and_also_two_equalities_emits_parenthesized_and_no_oversample() {
        var result = Translate(r => r.Category == "c" && r.Id == "k");

        await Assert.That(result.WhereClause).IsEqualTo("(category = ? AND id = ?)");
        await Assert.That(result.RequiresOversample).IsFalse();
        await Assert.That(result.Parameters.Count).IsEqualTo(2);
        await Assert.That(result.Parameters[0]).IsEqualTo("c");
        await Assert.That(result.Parameters[1]).IsEqualTo("k");
    }

    [Test]
    public async ValueTask and_also_equality_and_containment_requires_oversample() {
        var result = Translate(r => r.Category == "c" && r.Tags.Contains("post"));

        await Assert.That(result.WhereClause).IsEqualTo("(category = ? AND array_has_any(tags, [?]))");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(2);
        await Assert.That(result.Parameters[0]).IsEqualTo("c");
        await Assert.That(result.Parameters[1]).IsEqualTo("post");
    }

    [Test]
    public async ValueTask and_also_nested_composition_translates_recursively() {
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
    public async ValueTask filter_on_non_indexed_property_throws() =>
        await Assert
            .That(() => Translate(r => r.Content == "x"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("IsIndexed");

    [Test]
    public async ValueTask filter_boolean_property_shortcut_throws() =>
        await Assert
            .That(() => Translate(r => r.Flag))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Flag");

    [Test]
    public async ValueTask filter_logical_or_throws() =>
        await Assert
            .That(() => Translate(r => r.Category == "a" || r.Category == "b"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("||");

    [Test]
    public async ValueTask filter_inequality_throws() =>
        await Assert
            .That(() => Translate(r => r.Category != "a"))
            .Throws<NotSupportedException>()
            .WithMessageContaining("!=");

    [Test]
    public async ValueTask filter_greater_than_throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Length > 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("greater-than");

    [Test]
    public async ValueTask filter_less_than_throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Length < 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("less-than");

    [Test]
    public async ValueTask filter_logical_not_throws() =>
        await Assert
            .That(() => Translate(r => !(r.Category == "a")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("NOT");

    [Test]
    public async ValueTask filter_constant_predicate_throws() =>
        await Assert
            .That(() => Translate(r => true))
            .Throws<NotSupportedException>()
            .WithMessageContaining("constant");

    [Test]
    public async ValueTask filter_nested_member_access_throws() =>
        await Assert
            .That(() => Translate(r => r.Category.Length == 3))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Length");

    [Test]
    public async ValueTask filter_property_to_property_throws() =>
        await Assert
            .That(() => Translate(r => r.Category == r.Content))
            .Throws<NotSupportedException>()
            .WithMessageContaining("property-to-property");

    [Test]
    public async ValueTask filter_string_contains_throws() =>
        await Assert
            .That(() => Translate(r => r.Content.Contains("x")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Contains");

    [Test]
    public async ValueTask filter_inline_array_contains_throws() =>
        await Assert
            .That(() => Translate(r => new[] { "a" }.Contains(r.Category)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("IN-style");

    [Test]
    public async ValueTask filter_contains_null_throws() =>
        await Assert
            .That(() => Translate(r => r.Tags.Contains(null!)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("non-null string");

    [Test]
    public async ValueTask filter_equality_with_null_throws() =>
        await Assert
            .That(() => Translate(r => r.Category == null))
            .Throws<NotSupportedException>()
            .WithMessageContaining("null");

    [Test]
    public async ValueTask filter_on_vector_property_throws() {
        float[] probe = [1f, 0f, 0f, 0f];

        await Assert
            .That(() => Translate(r => r.Vec == probe))
            .Throws<NotSupportedException>()
            .WithMessageContaining("vector");
    }

    [Test]
    public async ValueTask filter_enumerable_any_throws() =>
        await Assert
            .That(() => Translate(r => r.Tags.Any(t => t == "x")))
            .Throws<NotSupportedException>()
            .WithMessageContaining("Any");

    [Test]
    public async ValueTask filter_datetime_comparison_on_non_indexed_property_throws() =>
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