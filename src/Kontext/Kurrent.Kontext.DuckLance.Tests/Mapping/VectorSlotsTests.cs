using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Mapping;

/// <summary>
/// Pure unit tests for <see cref="VectorSlots"/>: the named-slot value the codec pipeline hands to
/// Encode. Construction is internal on purpose — implementers only consume slots — so these tests
/// build instances through the internal ctor (InternalsVisibleTo).
/// </summary>
public class VectorSlotsTests {
    [Test]
    public async Task Default_Value_Is_Empty_With_Null_Single() {
        var slots = default(VectorSlots);

        await Assert.That(slots.Count).IsEqualTo(0);
        await Assert.That(slots.Single).IsNull();
    }

    [Test]
    public async Task Single_Slot_Serves_Single_And_Named_Access() {
        float[] vector = [1f, 2f];

        var slots = new VectorSlots(["vec"], [vector]);

        await Assert.That(slots.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(slots.Single, vector)).IsTrue();
        await Assert.That(ReferenceEquals(slots["vec"], vector)).IsTrue();
    }

    [Test]
    public async Task Multi_Slot_Serves_Each_Column_By_Name_And_Refuses_Single() {
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
    public async Task Unknown_Column_Throws_Instead_Of_Returning_Null() {
        var slots = new VectorSlots(["vec"], [[1f]]);

        // A typo'd column name must fail loudly — returning null here would silently write a NULL vector.
        await Assert.That(() => { _ = slots["vce"]; }).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task A_Slot_Can_Hold_A_Null_Vector() {
        var slots = new VectorSlots(["vec"], [null]);

        await Assert.That(slots.Single).IsNull();
        await Assert.That(slots["vec"]).IsNull();
    }
}
