// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Shouldly;

namespace KurrentDB.Testing.Shouldly;

public class ShouldlyObjectGraphTestExtensionsTests {
    [Test]
    public void should_behave_exactly_like_original_when_no_configuration_provided() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var user1 = new User { Id = 1, Name = "John", Email = "john@example.com", CreatedAt = fixedDate, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate } };
        var user2 = new User { Id = 1, Name = "John", Email = "john@example.com", CreatedAt = fixedDate, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate } };

        // Act & Assert - Should work exactly like the original
        user1.ShouldBeEquivalentTo(user2);
    }

    [Test]
    public void should_exclude_properties_by_expression() {
        // Arrange
        var fixedDate1 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var fixedDate2 = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc);
        var user1 = new User { Id = 1, Name   = "John", Email = "john@example.com", CreatedAt = fixedDate1, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate1 } };
        var user2 = new User { Id = 999, Name = "John", Email = "john@example.com", CreatedAt = fixedDate2, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate1 } };

        // Act & Assert - Should ignore Id and CreatedAt differences
        user1.ShouldBeEquivalentTo(
            user2, config => config
                .Excluding<User>(x => x.Id)
                .Excluding<User>(x => x.CreatedAt)
        );
    }

    [Test]
    public void should_exclude_nested_properties_by_path() {
        // Arrange
        var fixedDate1 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var fixedDate2 = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc);
        var user1 = new User {
            Id        = 1,
            Name      = "John",
            CreatedAt = fixedDate1,
            Profile   = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate1 }
        };

        var user2 = new User {
            Id        = 1,
            Name      = "John",
            CreatedAt = fixedDate1,
            Profile   = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate2 }
        };

        // Act & Assert - Should ignore nested Profile.LastModified
        user1.ShouldBeEquivalentTo(
            user2, config => config
                .Excluding("Profile.LastModified")
        );
    }

    [Test]
    public void should_use_custom_string_comparison() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var user1 = new User { Id = 1, Name = "john", Email = "JOHN@EXAMPLE.COM", CreatedAt = fixedDate, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate } };
        var user2 = new User { Id = 1, Name = "JOHN", Email = "john@example.com", CreatedAt = fixedDate, Profile = new Profile { FirstName = "John", LastName = "Doe", LastModified = fixedDate } };

        // Act & Assert - Should ignore case differences
        user1.ShouldBeEquivalentTo(
            user2, config => config
                .WithStringComparison(StringComparison.OrdinalIgnoreCase)
        );
    }

    [Test]
    public void should_use_numeric_tolerance_when_configured() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var order1 = new Order { Id = 1, Total = 100.0m, CreatedAt = fixedDate, Items = new List<string>() };
        var order2 = new Order { Id = 1, Total = 100.05m, CreatedAt = fixedDate, Items = new List<string>() };

        // Act & Assert - Should allow small numeric differences
        order1.ShouldBeEquivalentTo(
            order2, config => config
                .WithNumericTolerance(0.1));
    }

    [Test]
    public void should_fail_without_numeric_tolerance_by_default() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var order1 = new Order { Id = 1, Total = 100.0m, CreatedAt = fixedDate, Items = new List<string>() };
        var order2 = new Order { Id = 1, Total = 100.05m, CreatedAt = fixedDate, Items = new List<string>() };

        // Act & Assert - Should fail with exact decimal comparison
        Should.Throw<ShouldAssertException>(() => { order1.ShouldBeEquivalentTo(order2); });
    }

    [Test]
    public void should_ignore_collection_order_when_configured() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var order1 = new Order { Id = 1, CreatedAt = fixedDate, Items = ["Apple", "Banana", "Cherry"] };
        var order2 = new Order { Id = 1, CreatedAt = fixedDate, Items = ["Cherry", "Apple", "Banana"] };

        // Act & Assert - Should ignore order differences
        order1.ShouldBeEquivalentTo(
            order2, config => config
                .IgnoringCollectionOrder());
    }

    [Test]
    public void should_respect_collection_order_by_default() {
        // Arrange
        var fixedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var order1 = new Order { Id = 1, CreatedAt = fixedDate, Items = ["Apple", "Banana", "Cherry"] };
        var order2 = new Order { Id = 1, CreatedAt = fixedDate, Items = ["Cherry", "Apple", "Banana"] };

        // Act & Assert - Should fail with different order
        Should.Throw<ShouldAssertException>(() => { order1.ShouldBeEquivalentTo(order2); });
    }

    [Test]
    public void should_use_custom_comparer_for_type() {
        // Arrange
        var order1 = new Order { Id = 1, CreatedAt = DateTime.Parse("2025-01-01T10:00:00Z"), Items = new List<string>() };
        var order2 = new Order { Id = 1, CreatedAt = DateTime.Parse("2025-01-01T10:00:30Z"), Items = new List<string>() };

        // Act & Assert - Should use custom DateTime comparer with 1-minute tolerance
        order1.ShouldBeEquivalentTo(
            order2, config => config
                .Using<DateTime>((x, y) => Math.Abs((x - y).TotalSeconds) < 60));
    }

    [Test]
    public void should_combine_multiple_configuration_options() {
        // Arrange
        var fixedDate1 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var fixedDate2 = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc);
        var fixedDate3 = new DateTime(2025, 1, 1, 12, 30, 0, DateTimeKind.Utc);
        var user1 = new User {
            Id        = 1,
            Name      = "john",
            Email     = "john@example.com",
            CreatedAt = fixedDate1,
            Profile = new Profile {
                FirstName    = "John",
                LastName     = "Doe",
                LastModified = fixedDate1
            }
        };

        var user2 = new User {
            Id        = 999,
            Name      = "JOHN",
            Email     = "JOHN@EXAMPLE.COM",
            CreatedAt = fixedDate2,
            Profile = new Profile {
                FirstName    = "John",
                LastName     = "Doe",
                LastModified = fixedDate3
            }
        };

        // Act & Assert - Should handle complex configuration
        user1.ShouldBeEquivalentTo(
            user2, config => config
                .Excluding<User>(x => x.Id)
                .Excluding("Profile.LastModified")
                .WithStringComparison(StringComparison.OrdinalIgnoreCase)
                .Using<DateTime>((x, y) => Math.Abs((x - y).TotalHours) < 2));
    }

    // Test classes for demonstration
    public record User {
        public int      Id        { get; init; }
        public string   Name      { get; init; } = "";
        public string   Email     { get; init; } = "";
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public Profile  Profile   { get; init; } = new();
    }

    public record Profile {
        public string   FirstName    { get; init; } = "";
        public string   LastName     { get; init; } = "";
        public DateTime LastModified { get; init; } = DateTime.UtcNow;
    }

    public record Order {
        public int          Id        { get; init; }
        public decimal      Total     { get; init; }
        public List<string> Items     { get; init; } = new();
        public DateTime     CreatedAt { get; init; } = DateTime.UtcNow;
    }
}
