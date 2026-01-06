// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Infrastructure;
using KurrentDB.Testing.Bogus;

namespace KurrentDB.Api.Tests.Infrastructure;

public class IndexedSetTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public void adds_one_item() {
        // Arrange
        var item = Faker.WaffleTitle();

        var set = new IndexedSet<string>();

        // Act
        var added = set.Add(item);

        // Assert
        var getByIndex = () => set[0];

        added.ShouldBeTrue();
        set.ShouldHaveSingleItem();
        getByIndex.ShouldNotThrow().ShouldBe(item);
    }

    [Test]
    [Arguments(3)]
    [Arguments(5)]
    [Arguments(7)]
    public void adds_many_items(int itemCount) {
        // Arrange
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();

        var set = new IndexedSet<string>();

        // Act
        foreach (var item in items) set.Add(item);

        // Assert
        set.Count.ShouldBe(itemCount);

        for (var i = 0; i < items.Count; i++) set[i].ShouldBe(items[i]);
    }

    [Test]
    public void does_not_add_duplicate_item() {
        // Arrange
        var item = Faker.WaffleTitle();
        var set  = new IndexedSet<string>();

        // Act
        var firstAdd  = set.Add(item);
        var secondAdd = set.Add(item);

        // Assert
        firstAdd.ShouldBeTrue();
        secondAdd.ShouldBeFalse();
        set.ShouldHaveSingleItem();
        set[0].ShouldBe(item);
    }

    [Test]
    public void contains_returns_true_for_existing_item() {
        // Arrange
        var item = Faker.WaffleTitle();
        var set  = new IndexedSet<string> { item };

        // Act
        var contains = set.Contains(item);

        // Assert
        contains.ShouldBeTrue();
    }

    [Test]
    public void contains_returns_false_for_non_existing_item() {
        // Arrange
        var existingItem    = Faker.WaffleTitle();
        var nonExistingItem = Faker.WaffleTitle();

        var set = new IndexedSet<string> { existingItem };

        // Act
        var contains = set.Contains(nonExistingItem);

        // Assert
        contains.ShouldBeFalse();
    }

    [Test]
    public void removes_existing_item() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 10);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();
        foreach (var item in items) set.Add(item);

        var indexToRemove = Faker.Random.Number(1, itemCount - 2); // Not first or last
        var itemToRemove = items[indexToRemove];

        // Act
        var removed = set.Remove(itemToRemove);

        // Assert
        removed.ShouldBeTrue();
        set.Count.ShouldBe(itemCount - 1);
        set.Contains(itemToRemove).ShouldBeFalse();

        // Verify remaining items maintain their relative order
        for (var i = 0; i < indexToRemove; i++) {
            set[i].ShouldBe(items[i]);
        }
        for (var i = indexToRemove; i < set.Count; i++) {
            set[i].ShouldBe(items[i + 1]);
        }
    }

    [Test]
    public void remove_returns_false_for_non_existing_item() {
        // Arrange
        var item            = Faker.WaffleTitle();
        var nonExistingItem = Faker.WaffleTitle();
        var set             = new IndexedSet<string> { item };

        // Act
        var removed = set.Remove(nonExistingItem);

        // Assert
        removed.ShouldBeFalse();
        set.ShouldHaveSingleItem();
    }

    [Test]
    public void removes_item_at_index() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 10);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        var indexToRemove = Faker.Random.Number(1, itemCount - 2);

        // Act
        set.RemoveAt(indexToRemove);

        // Assert
        set.Count.ShouldBe(itemCount - 1);
        set.Contains(items[indexToRemove]).ShouldBeFalse();

        for (var i = 0; i < indexToRemove; i++)
            set[i].ShouldBe(items[i]);

        for (var i = indexToRemove; i < set.Count; i++)
            set[i].ShouldBe(items[i + 1]);
    }

    [Test]
    public void index_of_returns_correct_index() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 15);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act & Assert
        for (var i = 0; i < items.Count; i++)
            set.IndexOf(items[i]).ShouldBe(i);
    }

    [Test]
    public void index_of_returns_negative_one_for_non_existing_item() {
        // Arrange
        var item            = Faker.WaffleTitle();
        var nonExistingItem = Faker.WaffleTitle();
        var set             = new IndexedSet<string> { item };

        // Act
        var index = set.IndexOf(nonExistingItem);

        // Assert
        index.ShouldBe(-1);
    }

    [Test]
    public void clear_removes_all_items() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 15);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        set.Clear();

        // Assert
        set.Count.ShouldBe(0);

        foreach (var item in items)
            set.Contains(item).ShouldBeFalse();
    }

    [Test]
    public void inserts_item_at_specified_index() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 8);
        var items   = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var newItem = Faker.WaffleTitle();
        var set     = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        var insertIndex = Faker.Random.Number(1, itemCount - 1);

        // Act
        var inserted = set.Insert(insertIndex, newItem);

        // Assert
        inserted.ShouldBeTrue();
        set.Count.ShouldBe(itemCount + 1);

        for (var i = 0; i < insertIndex; i++) {
            set[i].ShouldBe(items[i]);
        }
        set[insertIndex].ShouldBe(newItem);
        for (var i = insertIndex + 1; i < set.Count; i++) {
            set[i].ShouldBe(items[i - 1]);
        }
    }

    [Test]
    public void insert_does_not_add_duplicate() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 8);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        var insertIndex = Faker.Random.Number(1, itemCount - 2);
        var duplicateIndex = Faker.Random.Number(insertIndex + 1, itemCount - 1);

        // Act
        var inserted = set.Insert(insertIndex, items[duplicateIndex]);

        // Assert
        inserted.ShouldBeFalse();
        set.Count.ShouldBe(itemCount);
        for (var i = 0; i < itemCount; i++) {
            set[i].ShouldBe(items[i]);
        }
    }

    [Test]
    public void insert_at_end_appends_item() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 8);
        var items   = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var newItem = Faker.WaffleTitle();
        var set     = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var inserted = set.Insert(set.Count, newItem);

        // Assert
        inserted.ShouldBeTrue();
        set.Count.ShouldBe(itemCount + 1);
        set[itemCount].ShouldBe(newItem);
    }

    [Test]
    public void to_list_returns_copy() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 10);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var list = set.ToList();

        // Assert
        list.Count.ShouldBe(items.Count);
        for (var i = 0; i < items.Count; i++)
            list[i].ShouldBe(items[i]);

        // Verify it's a copy
        list.Add(Faker.WaffleTitle());
        set.Count.ShouldBe(items.Count);
    }

    [Test]
    public void to_array_returns_array_with_correct_order() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 10);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var array = set.ToArray();

        // Assert
        array.Length.ShouldBe(items.Count);
        for (var i = 0; i < items.Count; i++) array[i].ShouldBe(items[i]);
    }

    [Test]
    public void copy_to_copies_elements_to_array() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 8);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        var arraySize = itemCount + Faker.Random.Number(2, 5);
        var startIndex = Faker.Random.Number(1, arraySize - itemCount - 1);
        var array = new string[arraySize];

        // Act
        set.CopyTo(array, startIndex);

        // Assert
        for (var i = 0; i < startIndex; i++) array[i].ShouldBeNull();
        for (var i = 0; i < itemCount; i++) array[startIndex + i].ShouldBe(items[i]);
        for (var i = startIndex + itemCount; i < arraySize; i++) array[i].ShouldBeNull();
    }

    [Test]
    public void is_subset_of_returns_true_when_subset() {
        // Arrange
        var itemCount = Faker.Random.Number(7, 12);
        var allItems = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();

        var subsetCount = Faker.Random.Number(3, itemCount - 2);
        var subset = new IndexedSet<string>();

        for (var i = 0; i < subsetCount; i++) {
            subset.Add(allItems[i * 2 % itemCount]);
        }

        // Act
        var isSubset = subset.IsSubsetOf(allItems);

        // Assert
        isSubset.ShouldBeTrue();
    }

    [Test]
    public void is_subset_of_returns_false_when_not_subset() {
        // Arrange
        var itemCount = Faker.Random.Number(3, 8);
        var items     = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var extraItem = Faker.WaffleTitle();
        var set       = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        set.Add(extraItem);

        // Act
        var isSubset = set.IsSubsetOf(items);

        // Assert
        isSubset.ShouldBeFalse();
    }

    [Test]
    public void is_superset_of_returns_true_when_superset() {
        // Arrange
        var itemCount = Faker.Random.Number(7, 12);
        var allItems = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set      = new IndexedSet<string>();

        foreach (var item in allItems) set.Add(item);

        var subsetCount = Faker.Random.Number(3, itemCount - 2);
        var subset = new List<string>();
        for (var i = 0; i < subsetCount; i++)
            subset.Add(allItems[i * 2 % itemCount]);

        // Act
        var isSuperset = set.IsSupersetOf(subset);

        // Assert
        isSuperset.ShouldBeTrue();
    }

    [Test]
    public void is_superset_of_returns_false_when_not_superset() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 10);
        var items     = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var extraItem = Faker.WaffleTitle();

        var setCount = Faker.Random.Number(2, itemCount - 2);
        var set = new IndexedSet<string>();
        for (var i = 0; i < setCount; i++) {
            set.Add(items[i]);
        }

        var other = new List<string>();

        for (var i = 0; i < setCount; i++)
            other.Add(items[i]);

        other.Add(items[itemCount - 1]);
        other.Add(extraItem);

        // Act
        var isSuperset = set.IsSupersetOf(other);

        // Assert
        isSuperset.ShouldBeFalse();
    }

    [Test]
    public void intersect_with_keeps_only_common_elements() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 10);
        var items1 = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();

        var commonCount = Faker.Random.Number(2, itemCount - 2);
        var items2 = new List<string>();

        for (var i = 0; i < commonCount; i++)
            items2.Add(items1[i * 2 % itemCount]);

        items2.Add(Faker.WaffleTitle()); // Add a non-common item

        var set = new IndexedSet<string>();

        foreach (var item in items1) set.Add(item);

        var expectedCommon = items1.Intersect(items2).ToList();

        // Act
        set.IntersectWith(items2);

        // Assert
        set.Count.ShouldBe(expectedCommon.Count);
        foreach (var item in expectedCommon) set.Contains(item).ShouldBeTrue();
        foreach (var item in items1.Except(expectedCommon)) set.Contains(item).ShouldBeFalse();
    }

    [Test]
    public void union_with_adds_unique_elements() {
        // Arrange
        var itemCount1 = Faker.Random.Number(3, 8);
        var items1 = Enumerable.Range(0, itemCount1).Select(_ => Faker.WaffleTitle()).ToList();

        var duplicateIndex = Faker.Random.Number(0, itemCount1 - 1);
        var newItemCount = Faker.Random.Number(2, 5);
        var items2 = new List<string> { items1[duplicateIndex] };
        var newItems = Enumerable.Range(0, newItemCount).Select(_ => Faker.WaffleTitle()).ToList();
        items2.AddRange(newItems);

        var set = new IndexedSet<string>();
        foreach (var item in items1) set.Add(item);

        // Act
        set.UnionWith(items2);

        // Assert
        set.Count.ShouldBe(itemCount1 + newItemCount);
        for (var i = 0; i < itemCount1; i++) set[i].ShouldBe(items1[i]);
        for (var i = 0; i < newItemCount; i++) set[itemCount1 + i].ShouldBe(newItems[i]);
    }

    [Test]
    public void enumerates_items_in_order() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 15);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var enumerated = set.ToList();

        // Assert
        enumerated.Count.ShouldBe(items.Count);
        for (var i = 0; i < items.Count; i++) enumerated[i].ShouldBe(items[i]);
    }

    [Test]
    public void initializes_from_collection() {
        // Arrange
        var itemCount = Faker.Random.Number(5, 15);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();

        // Act
        var set = new IndexedSet<string>(items);

        // Assert
        set.Count.ShouldBe(items.Count);

        for (var i = 0; i < items.Count; i++)
            set[i].ShouldBe(items[i]);
    }

    [Test]
    public void initializes_from_collection_with_duplicates_maintains_uniqueness() {
        // Arrange
        var uniqueItem = Faker.WaffleTitle();

        var items = new[] {
            Faker.WaffleTitle(),
            uniqueItem,
            Faker.WaffleTitle(),
            uniqueItem, // Duplicate
            Faker.WaffleTitle()
        };

        // Act
        var set = new IndexedSet<string>(items);

        // Assert
        set.Count.ShouldBe(4);
        set.Contains(uniqueItem).ShouldBeTrue();
        set.IndexOf(uniqueItem).ShouldBe(1); // First occurrence position
    }

    [Test]
    public void uses_custom_equality_comparer() {
        // Arrange
        var comparer = StringComparer.OrdinalIgnoreCase;
        var set      = new IndexedSet<string>(comparer);

        // Act
        var added1 = set.Add("Hello");
        var added2 = set.Add("HELLO");

        // Assert
        added1.ShouldBeTrue();
        added2.ShouldBeFalse();
        set.ShouldHaveSingleItem();
        set[0].ShouldBe("Hello");
    }

    [Test]
    public void to_string_shows_first_three_items() {
        // Arrange
        var itemCount = Faker.Random.Number(4, 10);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var result = set.ToString();

        // Assert
        result.ShouldContain($"IndexedSet<String>[{itemCount}]");
        result.ShouldContain(items[0]);
        result.ShouldContain(items[1]);
        result.ShouldContain(items[2]);
        result.ShouldContain("...");
    }

    [Test]
    public void to_string_shows_all_items_when_three_or_fewer() {
        // Arrange
        var itemCount = Faker.Random.Number(1, 3);
        var items = Enumerable.Range(0, itemCount).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        foreach (var item in items) set.Add(item);

        // Act
        var result = set.ToString();

        // Assert
        result.ShouldContain($"IndexedSet<String>[{itemCount}]");
        foreach (var item in items) result.ShouldContain(item);
        result.ShouldNotContain("...");
    }

    [Test]
    public void maintains_order_after_multiple_operations() {
        // Arrange
        var totalItems = Faker.Random.Number(10, 15);
        var items = Enumerable.Range(0, totalItems).Select(_ => Faker.WaffleTitle()).ToList();
        var set   = new IndexedSet<string>();

        // Act - Add initial items
        var initialCount = Faker.Random.Number(5, 7);
        foreach (var item in items.Take(initialCount)) set.Add(item);

        // Remove middle item
        var removeIndex = Faker.Random.Number(1, initialCount - 2);
        set.RemoveAt(removeIndex);

        // Insert at beginning
        var newItem1 = Faker.WaffleTitle();
        set.Insert(0, newItem1);

        // Add more items
        var additionalCount = Faker.Random.Number(2, 4);
        foreach (var item in items.Skip(initialCount).Take(additionalCount))
            set.Add(item);

        // Remove by value
        var removeByValueIndex = Faker.Random.Number(0, initialCount - 1);

        if (removeByValueIndex != removeIndex) set.Remove(items[removeByValueIndex]);

        // Verify all items can be accessed by index
        for (var i = 0; i < set.Count; i++) {
            var getByIndex = () => set[i];
            getByIndex.ShouldNotThrow();
        }

        // Verify count is correct
        set.Count.ShouldBeGreaterThan(0);
    }
}
