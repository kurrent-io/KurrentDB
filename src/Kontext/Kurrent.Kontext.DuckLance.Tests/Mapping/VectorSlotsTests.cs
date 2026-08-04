using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// Pure unit tests for <see cref="VectorSlots"/>: the named-slot value the codec pipeline hands to
/// Encode. Construction is internal on purpose — implementers only consume slots — so these tests
/// build instances through the internal ctor (InternalsVisibleTo).
/// </summary>
[Category("Mapping")]
public class VectorSlotsTests {
    [Test]
    public async ValueTask default_value_is_empty_with_null_single() {
        var slots = default(VectorSlots);

        await Assert.That(slots.Count).IsEqualTo(0);
        await Assert.That(slots.Single).IsNull();
    }

    [Test]
    public async ValueTask single_slot_serves_single_and_named_access() {
        float[] vector = [1f, 2f];

        var slots = new VectorSlots(["vec"], [vector]);

        await Assert.That(slots.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(slots.Single, vector)).IsTrue();
        await Assert.That(ReferenceEquals(slots["vec"], vector)).IsTrue();
    }

    [Test]
    public async ValueTask multi_slot_serves_each_column_by_name_and_refuses_single() {
        float[] title = [1f];
        float[] body  = [2f];

        var slots = new VectorSlots(["title_vec", "body_vec"], [title, body]);

        await Assert.That(slots.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(slots["title_vec"], title)).IsTrue();
        await Assert.That(ReferenceEquals(slots["body_vec"], body)).IsTrue();

        // Single is only for records with one (or no) vector column; on a multi-vector record it
        // throws rather than guessing which slot was meant.
        await Assert.That(() => { _ = slots.Single; }).Throws<InvalidOperationException>();
    }

    [Test]
    public async ValueTask unknown_column_throws_instead_of_returning_null() {
        var slots = new VectorSlots(["vec"], [[1f]]);

        // A typo'd column name must fail loudly — returning null here would silently write a NULL vector.
        await Assert.That(() => { _ = slots["vce"]; }).Throws<KeyNotFoundException>();
    }

    [Test]
    public async ValueTask slot_can_hold_null_vector() {
        var slots = new VectorSlots(["vec"], [null]);

        await Assert.That(slots.Single).IsNull();
        await Assert.That(slots["vec"]).IsNull();
    }
}
