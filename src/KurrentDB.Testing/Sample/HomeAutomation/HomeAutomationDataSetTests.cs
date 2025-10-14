// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using KurrentDB.Testing.Bogus;
using Serilog;

namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Test class to verify HomeAutomation DataSet functionality
/// </summary>
[Skip("Only run manually to verify the HomeAutomationDataSet functionality")]
public class HomeAutomationDataSetTests {
    BogusFaker Faker { get; init; } = new();

    [Test]
    public async Task basic_home_faker_generates_valid_home() {
        // Arrange
        var homeFaker = new HomeFaker();

        // Act
        var home = homeFaker.Generate();

        // Assert
        Log.Information(
            "Generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
            home.Name, home.Rooms.Count, home.Devices.Count);

        await Assert.That(home.Name).IsNotNull();
        await Assert.That(home.Rooms.Count).IsGreaterThanOrEqualTo(3); // Base rooms + additional
        await Assert.That(home.Devices.Count).IsGreaterThan(0);
        await Assert.That(home.CreatedAt).IsLessThan(DateTime.UtcNow);

        // Verify all devices have valid properties
        foreach (var device in home.Devices) {
            await Assert.That(device.Id).IsNotNull();
            await Assert.That(device.FriendlyName).IsNotNull();
            await Assert.That(device.Room).IsNotNull();
        }
    }

    [Test]
    public async Task home_automation_dataset_direct_usage_generates_valid_home() {
        // Arrange
        var dataset = new HomeAutomationDataSet();

        // Act
        var home = dataset.Home();

        // Assert
        Log.Information(
            "DataSet generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
            home.Name, home.Rooms.Count, home.Devices.Count);

        await Assert.That(home).IsNotNull();
        await Assert.That(home.Name).IsNotNull();
        await Assert.That(home.Rooms).IsNotEmpty();
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task faker_extension_provides_home_automation_access() {
        // Act
        var home = Faker.HomeAutomation().Home();

        // Assert
        Log.Information("Extension generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
            home.Name, home.Rooms.Count, home.Devices.Count);

        await Assert.That(home).IsNotNull();
        await Assert.That(home.Name).IsNotNull();
        await Assert.That(home.Rooms).IsNotEmpty();
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task flexible_home_generation_with_specific_room_count_respects_parameter() {
        // Arrange
        const int expectedRooms = 5;

        // Act
        var home = Faker.HomeAutomation().Home(rooms: expectedRooms);

        // Assert
        Log.Information("Home with {ExpectedRooms} rooms: {ActualRooms} rooms generated",
            expectedRooms, home.Rooms.Count);

        await Assert.That(home.Rooms.Count).IsEqualTo(expectedRooms);
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task flexible_home_generation_with_devices_per_room_generates_approximate_device_count() {
        // Arrange
        const int rooms = 3;
        const int devicesPerRoom = 2;
        const int expectedDevices = rooms * devicesPerRoom;

        // Act
        var home = Faker.HomeAutomation().Home(rooms: rooms, devicesPerRoom: devicesPerRoom);

        // Assert
        Log.Information("Home with {Rooms} rooms, {DevicesPerRoom} devices per room: {ActualDevices} devices generated",
            rooms, devicesPerRoom, home.Devices.Count);

        await Assert.That(home.Rooms.Count).IsEqualTo(rooms);
        await Assert.That(home.Devices.Count).IsEqualTo(expectedDevices);
    }

    [Test]
    public async Task flexible_home_generation_with_specific_device_types_only_generates_allowed_types() {
        // Arrange
        var faker = new Faker();
        var allowedTypes = new[] { DeviceType.SmartLight, DeviceType.MotionSensor };

        // Act
        var home = faker.HomeAutomation().Home(deviceTypes: allowedTypes);
        var actualDeviceTypes = home.Devices.Select(d => d.DeviceType).Distinct().ToList();

        // Assert
        Log.Information("Home with limited device types: {DeviceTypes}",
            string.Join(", ", actualDeviceTypes));

        await Assert.That(home.Devices).IsNotEmpty();
        foreach (var deviceType in actualDeviceTypes) {
            await Assert.That(allowedTypes).Contains(deviceType);
        }
    }

    [Test]
    public async Task device_generation_random_generates_valid_devices() {
        // Act
        var devices = Faker.HomeAutomation().Devices();

        // Assert
        Log.Information("Generated {DeviceCount} random devices", devices.Count);

        await Assert.That(devices).IsNotEmpty();
        foreach (var device in devices) {
            await Assert.That(device.Id).IsNotNull();
            await Assert.That(device.FriendlyName).IsNotNull();
            await Assert.That(device.Room).IsNotNull();
        }
    }

    [Test]
    public async Task device_generation_with_specific_count_respects_count() {
        // Arrange
        var faker = new Faker();
        const int expectedCount = 10;

        // Act
        var devices = faker.HomeAutomation().Devices(count: expectedCount);

        // Assert
        Log.Information("Generated {ActualCount} devices with specific count {ExpectedCount}",
            devices.Count, expectedCount);

        await Assert.That(devices.Count).IsEqualTo(expectedCount);
    }

    [Test]
    public async Task device_generation_with_specific_types_only_generates_allowed_types() {
        // Arrange
        var allowedTypes = new[] { DeviceType.Thermostat, DeviceType.SmartLight };
        const int count = 5;

        // Act
        var devices        = Faker.HomeAutomation().Devices(count: count, types: allowedTypes);
        var generatedTypes = devices.Select(d => d.DeviceType).Distinct().ToList();

        // Assert
        Log.Information("Generated devices with specific types: {DeviceTypes}",
            string.Join(", ", generatedTypes));

        await Assert.That(devices.Count).IsEqualTo(count);
        foreach (var deviceType in generatedTypes) {
            await Assert.That(allowedTypes).Contains(deviceType);
        }
    }

    [Test]
    public async Task event_generation_for_random_devices_generates_valid_events() {
        // Arrange
        const int eventCount = 5;

        // Act
        var events = Faker.HomeAutomation().Events(count: eventCount);

        // Assert
        Log.Information("Generated {EventCount} events for random devices", events.Count);

        await Assert.That(events.Count).IsEqualTo(eventCount);
        foreach (var evt in events) {
            await Assert.That(evt).IsNotNull();
        }
    }

    [Test]
    public async Task event_generation_for_specific_home_generates_valid_events() {
        // Arrange
        var       home       = Faker.HomeAutomation().Home();
        const int eventCount = 10;

        // Act
        var events = Faker.HomeAutomation().Events(home, count: eventCount);

        // Assert
        Log.Information("Generated {EventCount} events for home '{HomeName}'",
            events.Count, home.Name);

        await Assert.That(events.Count).IsGreaterThanOrEqualTo(eventCount);
        foreach (var evt in events) {
            await Assert.That(evt).IsNotNull();
        }
    }

    [Test]
    public async Task event_generation_for_specific_devices_generates_valid_events() {
        // Arrange
        var devices = Faker.HomeAutomation().Devices(3);

        const int eventCount = 8;

        // Act
        var events = Faker.HomeAutomation().Events(devices, count: eventCount);

        // Assert
        Log.Information("Generated {EventCount} events for {DeviceCount} specific devices",
            events.Count, devices.Count);

        await Assert.That(events.Count).IsEqualTo(eventCount);

        if (events.Any()) {
            var sampleEvent = events.First();
            Log.Information("Sample event type: {EventType}", sampleEvent.GetType().Name);
            await Assert.That(sampleEvent).IsNotNull();
        }
    }
}
