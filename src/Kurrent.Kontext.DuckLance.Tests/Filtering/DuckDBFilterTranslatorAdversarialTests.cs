using System.Linq.Expressions;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Filtering;

/// <summary>
/// ADVERSARIAL probes for <see cref="DuckDBFilterTranslator"/> and its wiring. Every probe is a pure,
/// deterministic unit test (no DuckDB connection) that either (a) documents a currently-correct behavior,
/// or (b) demonstrates a defect. Tests whose name ends in <c>_BUG</c> capture buggy-but-actual behavior and
/// PASS; tests whose name ends in <c>_CONTRACT</c> assert the behavior the class contract promises and
/// therefore FAIL until the defect is fixed.
/// </summary>
/// <remarks>
/// The contract under test: exactly two filter shapes are supported — property equality on the key/IsIndexed
/// properties (<c>col = ?</c>, true prefilter) and tag containment on IsIndexed <c>List&lt;string&gt;</c>/<c>string[]</c>
/// (<c>array_has_any(col, [?])</c>, whole-table oversample) — combined with <c>&amp;&amp;</c>. Everything else must
/// throw <see cref="NotSupportedException"/> at translation time; nothing may silently emit wrong SQL or a partial
/// filter, and no user value may be inlined rather than parameterized.
/// </remarks>
public class DuckDBFilterTranslatorAdversarialTests {
    static CollectionModel Model() =>
        new DuckDBModelBuilder().Build(
            typeof(AdversarialRecord), typeof(string), null,
            null);

    static DuckDBFilterResult Translate(Expression<Func<AdversarialRecord, bool>> filter) => new DuckDBFilterTranslator().Translate(filter, Model());

    // =====================================================================================================
    // Value-side casts are EVALUATED, not discarded. A Convert/ConvertChecked on the VALUE operand is applied with
    // exact C# cast semantics before the parameter is bound, so `== (int)5.9` binds the int 5 (binding the pre-cast
    // 5.9 made DuckDB evaluate `num = 5.9` and silently drop every num == 5 row), an overflowing UNCHECKED cast binds
    // its wrapped value, and a CHECKED overflow surfaces as NotSupportedException. These are the regression guards for
    // the former silent-misfilter defect (ExtractValue used to unwrap ALL Convert nodes and bind the pre-cast operand).
    // =====================================================================================================

    [Test]
    public async Task Value_LossyIntCastFromDouble_BindsEvaluatedInt() {
        var d      = 5.9;
        var result = Translate(r => r.Num == (int)d);

        // The value-side cast `(int)d` is evaluated exactly: the bound parameter is the int 5, NOT the pre-cast
        // double 5.9 (binding 5.9 made DuckDB evaluate `num = 5.9` and silently drop every num == 5 row).
        await Assert.That(result.WhereClause).IsEqualTo("num = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(int));
        await Assert.That((int)result.Parameters[0]!).IsEqualTo(5);
    }

    [Test]
    public async Task Value_LossyIntCastFromDouble_BindsFive_CONTRACT() {
        var d      = 5.9;
        var result = Translate(r => r.Num == (int)d);

        // Contract expectation: a faithful translation binds the operand's value, the int 5.
        await Assert.That(result.Parameters[0]).IsEqualTo(5);
    }

    [Test]
    public async Task Value_LossyIntCastFromOverflowingLong_BindsUncheckedWrappedInt() {
        var big    = 5_000_000_000L; // > int.MaxValue
        var result = Translate(r => r.Num == (int)big);

        // `(int)big` in the (default) unchecked context is an ExpressionType.Convert node, so evaluating it wraps
        // exactly as C# does: 5_000_000_000 - 2^32 == 705032704. We bind that wrapped int — the operand's real
        // runtime value — instead of the pre-cast long 5_000_000_000 that the old code bound and misfiltered on.
        await Assert.That(result.WhereClause).IsEqualTo("num = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(int));
        await Assert.That((int)result.Parameters[0]!).IsEqualTo(705032704);
    }

    [Test]
    public async Task Value_CheckedIntCastOverflow_ThrowsNotSupported() {
        var big = 5_000_000_000L; // > int.MaxValue

        // A checked cast is an ExpressionType.ConvertChecked node; evaluating it overflows at translation time, and
        // an overflowing value is not bindable, so it surfaces as the class-contract NotSupportedException.
        await Assert
            .That(() => Translate(r => r.Num == checked((int)big)))
            .Throws<NotSupportedException>();
    }

    // =====================================================================================================
    // Small-integer / widening equality now TRANSLATES. Standard numeric promotions wrap the PROPERTY side in a
    // Convert (short/byte -> int, int -> long) that the base binder rejects with InvalidCastException. The relaxed
    // binder unwraps the promotion, binds the property, and narrows the value back to the property's own CLR type
    // with a checked conversion: an IsIndexed short is filterable (short-typed parameter), a value that cannot fit
    // the property's type throws NotSupportedException, and no base-library InvalidCastException escapes the contract.
    // =====================================================================================================

    [Test]
    public async Task Equality_ShortProperty_TranslatesToShortTypedPlaceholder() {
        short s      = 3;
        var   result = Translate(r => r.Small == s);

        // `short == short` promotes both sides to int; the promotion is unwrapped and the value narrowed back to a
        // short, so the bound parameter matches the short column's storage type.
        await Assert.That(result.WhereClause).IsEqualTo("small = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(short));
        await Assert.That((short)result.Parameters[0]!).IsEqualTo((short)3);
    }

    [Test]
    public async Task Equality_ShortProperty_Translates_CONTRACT() {
        short s = 3;
        // Contract: an IsIndexed short is a filterable property; equality on it translates (it previously threw
        // InvalidCastException from the base binder). Operand order is irrelevant — `value == property` works too.
        var result = Translate(r => s == r.Small);

        await Assert.That(result.WhereClause).IsEqualTo("small = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(short));
        await Assert.That((short)result.Parameters[0]!).IsEqualTo((short)3);
    }

    [Test]
    public async Task Equality_ShortProperty_NonFittingValue_ThrowsNotSupported() {
        var tooBig = 100_000; // > short.MaxValue (32767)

        // `short == int` promotes the short column to int; narrowing the value 100000 back to short overflows, and a
        // value that cannot equal any short is rejected (NotSupportedException) rather than silently wrapped.
        await Assert
            .That(() => Translate(r => r.Small == tooBig))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Equality_IntPropertyVsFittingLongValue_BindsIntValue() {
        long l      = 7;
        var  result = Translate(r => r.Num == l);

        // `int == long` promotes the int column to long; the promotion is unwrapped and the (fitting) long value is
        // narrowed back to int, so the bound parameter matches the int column.
        await Assert.That(result.WhereClause).IsEqualTo("num = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(int));
        await Assert.That((int)result.Parameters[0]!).IsEqualTo(7);
    }

    [Test]
    public async Task Containment_IEnumerableUpcastSource_ThrowsNotSupported() =>
        // ((IEnumerable<string>)r.Tags).Contains("x") wraps the source in Convert(Tags, IEnumerable<string>). That
        // property-side cast is NOT a numeric promotion, so the relaxed binder rethrows the base's InvalidCastException
        // as the class-contract NotSupportedException (v1 deliberately does not add upcast support).
        await Assert
            .That(() => Translate(r => ((IEnumerable<string>)r.Tags).Contains("x")))
            .Throws<NotSupportedException>();

    // =====================================================================================================
    // CORRECT behaviors — documented so a future change that breaks them is caught.
    // =====================================================================================================

    [Test]
    public async Task Equality_NullableIntProperty_BindsIntValue() {
        var result = Translate(r => r.NullableNum == 5);

        await Assert.That(result.WhereClause).IsEqualTo("nullablenum = ?");
        await Assert.That(result.Parameters[0]!.GetType()).IsEqualTo(typeof(int));
        await Assert.That((int)result.Parameters[0]!).IsEqualTo(5);
    }

    [Test]
    public async Task Containment_StringArrayProperty_EmitsArrayHasAny() {
        // On net10, string[].Contains binds to MemoryExtensions.Contains(ReadOnlySpan<string>, string); the base
        // TryMatchContains unwraps the implicit span cast back to the raw array member, so this DOES translate.
        var result = Translate(r => r.TagsArray.Contains("x"));

        await Assert.That(result.WhereClause).IsEqualTo("array_has_any(tagsarray, [?])");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters[0]).IsEqualTo("x");
    }

    [Test]
    public async Task Containment_EnumerableStaticContains_EmitsArrayHasAny() {
        var result = Translate(r => Enumerable.Contains(r.Tags, "x"));

        await Assert.That(result.WhereClause).IsEqualTo("array_has_any(tags, [?])");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters[0]).IsEqualTo("x");
    }

    [Test]
    public async Task Containment_ListIntProperty_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.IntTags.Contains(5)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("non-null string");

    [Test]
    public async Task Containment_ByteArrayScalar_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.Blob.Contains((byte)1)))
            .Throws<NotSupportedException>()
            .WithMessageContaining("scalar");

    [Test]
    public async Task Equality_BoolPropertyEqualsTrue_EmitsColumnEqualsPlaceholder() {
        var result = Translate(r => r.Flag == true);

        await Assert.That(result.WhereClause).IsEqualTo("flag = ?");
        await Assert.That((bool)result.Parameters[0]!).IsTrue();
    }

    [Test]
    public async Task Value_CapturedDictionaryIndexer_ThrowsNotSupported() {
        var dict = new Dictionary<string, string> { ["k"] = "v" };

        await Assert
            .That(() => Translate(r => r.Category == dict["k"]))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Value_MethodCallResult_ThrowsNotSupported() {
        var n = 5;

        await Assert
            .That(() => Translate(r => r.Category == n.ToString()))
            .Throws<NotSupportedException>();
    }

    // ---- Boundary leaks: every one must throw NotSupportedException, never simplify or partially filter ----

    [Test]
    public async Task Boundary_AndTrue_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.Category == "a" && true))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_EqualityToTrue_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.Category == "a" == true))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_DoubleNegation_ThrowsNotSupported_NotSimplifiedToEquality() =>
        await Assert
            .That(() => Translate(r => !(r.Category != "a")))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_StaticStringEquals_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => string.Equals(r.Category, "a", StringComparison.Ordinal)))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_InstanceEquals_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.Category.Equals("a", StringComparison.Ordinal)))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_Ternary_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => r.Category == "a" ? true : false))
            .Throws<NotSupportedException>();

    [Test]
    public async Task Boundary_NullCoalesce_ThrowsNotSupported() {
        string? maybeNull = null;

        await Assert
            .That(() => Translate(r => (maybeNull ?? r.Category) == "y"))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Boundary_BitwiseAnd_ThrowsNotSupported() =>
        await Assert
            .That(() => Translate(r => (r.Category == "a") & (r.Id == "b")))
            .Throws<NotSupportedException>();

    // =====================================================================================================
    // Wiring / model interactions.
    // =====================================================================================================

    [Test]
    public async Task Wiring_MixedPredicateOrder_ParametersAreLeftToRight() {
        var result = Translate(r => r.Category == "A" && r.Tags.Contains("B") && r.Id == "C");

        await Assert.That(result.WhereClause).IsEqualTo("((category = ? AND array_has_any(tags, [?])) AND id = ?)");
        await Assert.That(result.RequiresOversample).IsTrue();
        await Assert.That(result.Parameters.Count).IsEqualTo(3);
        await Assert.That(result.Parameters[0]).IsEqualTo("A");
        await Assert.That(result.Parameters[1]).IsEqualTo("B");
        await Assert.That(result.Parameters[2]).IsEqualTo("C");
    }

    [Test]
    public async Task Sql_HostileStorageNameViaDefinition_DiesAtModelBuild() {
        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)) { StorageName = "evil'; DROP TABLE x; --", IsIndexed = true },
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        // The hostile storage name must die in the model builder's charset gate, long before any translator sees it.
        await Assert
            .That(() => new DuckDBModelBuilder().BuildDynamic(definition, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Model_TypedBuildWithDefinition_StillBindsClrAttributedPropertyAbsentFromDefinition_BUG() {
        // The definition deliberately OMITS `Ghost`, which is attributed on the CLR type. One might expect the
        // definition to be the exclusive source of truth (item 7). It is NOT for the typed Build overload: the
        // builder unions the CLR-attributed properties, so `Ghost` is in the model and the filter translates.
        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        var model = new DuckDBModelBuilder().Build(
            typeof(GhostRecord), typeof(string), definition,
            null);

        var result = new DuckDBFilterTranslator().Translate((Expression<Func<GhostRecord, bool>>)(r => r.Ghost == "x"), model);

        await Assert.That(result.WhereClause).IsEqualTo("ghost = ?");
        await Assert.That(result.Parameters[0]).IsEqualTo("x");
    }

    [Test]
    public async Task Model_DynamicBuild_FilterOnKeyAbsentFromDefinition_Throws() {
        // The truly definition-only (dynamic) path IS the source of truth: a get_Item key not in the definition
        // must fail binding. It throws InvalidOperationException from the base binder — note this is NOT the
        // NotSupportedException the class contract advertises for unsupported filter constructs.
        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        var model = new DuckDBModelBuilder().BuildDynamic(definition, null);

        await Assert
            .That(() => new DuckDBFilterTranslator().Translate((Expression<Func<Dictionary<string, object?>, bool>>)(r => (string)r["ghost"]! == "x"), model))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OrderBy_NestedMemberSelector_Throws() {
        var model = Model();

        // The GetAsync OrderBy path resolves each sort key through GetDataOrKeyProperty; a nested-member selector
        // must throw (no garbage ORDER BY), which it does — InvalidOperationException "Property selector lambda is invalid".
        await Assert
            .That(() => model.GetDataOrKeyProperty<AdversarialRecord>(r => r.Category.Length))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OrderBy_VectorPropertySelector_ThrowsNotSupported() {
        var model = Model();
        // GetDataOrKeyProperty resolves a vector selector to its VectorPropertyModel rather than rejecting it, so the
        // OrderBy clause builder must reject it — otherwise it would emit `vec ASC`, ordering by a FLOAT[] column,
        // which is meaningless. The rejection lives in BuildOrderByClause, driven here directly (no DB connection).
        var options = new FilteredRecordRetrievalOptions<AdversarialRecord> { OrderBy = o => o.Descending(r => r.Vec) };

        await Assert
            .That(() => DuckDBCollection<string, AdversarialRecord>.BuildOrderByClause(model, options))
            .Throws<NotSupportedException>();
    }

    /// <summary>Rich adversarial POCO covering every filterable/non-filterable shape the probes exercise.</summary>
    sealed class AdversarialRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "tagsarray", IsIndexed = true)]
        public string[] TagsArray { get; set; } = [];

        [VectorStoreData(StorageName = "inttags", IsIndexed = true)]
        public List<int> IntTags { get; set; } = [];

        [VectorStoreData(StorageName = "num", IsIndexed = true)]
        public int Num { get; set; }

        [VectorStoreData(StorageName = "small", IsIndexed = true)]
        public short Small { get; set; }

        [VectorStoreData(StorageName = "big", IsIndexed = true)]
        public long Big { get; set; }

        [VectorStoreData(StorageName = "nullablenum", IsIndexed = true)]
        public int? NullableNum { get; set; }

        [VectorStoreData(StorageName = "flag", IsIndexed = true)]
        public bool Flag { get; set; }

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "";

        [VectorStoreData(StorageName = "blob")]
        public byte[] Blob { get; set; } = [];

        [VectorStoreVector(4, StorageName = "vec")]
        public float[] Vec { get; set; } = [];
    }

    /// <summary>CLR type with a property (<see cref="Ghost"/>) deliberately absent from the definition under test.</summary>
    sealed class GhostRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "ghost", IsIndexed = true)]
        public string Ghost { get; set; } = "";

        // Must match the definition's declared Vec type (ReadOnlyMemory<float>), else Build fails on a CLR-type
        // mismatch before the point this probe is testing (binding a property the definition omits).
        [VectorStoreVector(4, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}