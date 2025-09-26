using Bogus;
using Serilog;

namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Test class to verify HomeAutomation DataSet functionality
/// </summary>
public class HomeAutomationDataSetTests {
    [Test]
    public async Task BasicHomeFaker_ShouldGenerateValidHome() {
        // Arrange
        var homeFaker = new HomeFaker();

        // Act
        var home = homeFaker.Generate();

        // Assert
        Log.Information("Generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
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
    public async Task HomeAutomationDataSet_DirectUsage_ShouldGenerateValidHome() {
        // Arrange
        var dataset = new HomeAutomationDataSet();

        // Act
        var home = dataset.Home();

        // Assert
        Log.Information("DataSet generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
            home.Name, home.Rooms.Count, home.Devices.Count);

        await Assert.That(home).IsNotNull();
        await Assert.That(home.Name).IsNotNull();
        await Assert.That(home.Rooms).IsNotEmpty();
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task FakerExtension_ShouldProvideHomeAutomationAccess() {
        // Arrange
        var faker = new Faker();

        // Act
        var home = faker.HomeAutomation().Home();

        // Assert
        Log.Information("Extension generated home: {HomeName} with {RoomCount} rooms and {DeviceCount} devices",
            home.Name, home.Rooms.Count, home.Devices.Count);

        await Assert.That(home).IsNotNull();
        await Assert.That(home.Name).IsNotNull();
        await Assert.That(home.Rooms).IsNotEmpty();
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task FlexibleHomeGeneration_WithSpecificRoomCount_ShouldRespectParameter() {
        // Arrange
        var faker = new Faker();
        const int expectedRooms = 5;

        // Act
        var home = faker.HomeAutomation().Home(rooms: expectedRooms);

        // Assert
        Log.Information("Home with {ExpectedRooms} rooms: {ActualRooms} rooms generated",
            expectedRooms, home.Rooms.Count);

        await Assert.That(home.Rooms.Count).IsEqualTo(expectedRooms);
        await Assert.That(home.Devices).IsNotEmpty();
    }

    [Test]
    public async Task FlexibleHomeGeneration_WithDevicesPerRoom_ShouldGenerateApproximateDeviceCount() {
        // Arrange
        var faker = new Faker();
        const int rooms = 3;
        const int devicesPerRoom = 2;
        const int expectedDevices = rooms * devicesPerRoom;

        // Act
        var home = faker.HomeAutomation().Home(rooms: rooms, devicesPerRoom: devicesPerRoom);

        // Assert
        Log.Information("Home with {Rooms} rooms, {DevicesPerRoom} devices per room: {ActualDevices} devices generated",
            rooms, devicesPerRoom, home.Devices.Count);

        await Assert.That(home.Rooms.Count).IsEqualTo(rooms);
        await Assert.That(home.Devices.Count).IsEqualTo(expectedDevices);
    }

    [Test]
    public async Task FlexibleHomeGeneration_WithSpecificDeviceTypes_ShouldOnlyGenerateAllowedTypes() {
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
    public async Task DeviceGeneration_Random_ShouldGenerateValidDevices() {
        // Arrange
        var faker = new Faker();

        // Act
        var devices = faker.HomeAutomation().Devices();

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
    public async Task DeviceGeneration_WithSpecificCount_ShouldRespectCount() {
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
    public async Task DeviceGeneration_WithSpecificTypes_ShouldOnlyGenerateAllowedTypes() {
        // Arrange
        var faker = new Faker();
        var allowedTypes = new[] { DeviceType.Thermostat, DeviceType.SmartLight };
        const int count = 5;

        // Act
        var devices = faker.HomeAutomation().Devices(count: count, types: allowedTypes);
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
    public async Task EventGeneration_ForRandomDevices_ShouldGenerateValidEvents() {
        // Arrange
        var faker = new Faker();
        const int eventCount = 5;

        // Act
        var events = faker.HomeAutomation().Events(count: eventCount);

        // Assert
        Log.Information("Generated {EventCount} events for random devices", events.Count);

        await Assert.That(events.Count).IsEqualTo(eventCount);
        foreach (var evt in events) {
            await Assert.That(evt).IsNotNull();
        }
    }

    [Test]
    public async Task EventGeneration_ForSpecificHome_ShouldGenerateValidEvents() {
        // Arrange
        var faker = new Faker();
        var home = faker.HomeAutomation().Home();
        const int eventCount = 10;

        // Act
        var events = faker.HomeAutomation().Events(home, count: eventCount);

        // Assert
        Log.Information("Generated {EventCount} events for home '{HomeName}'",
            events.Count, home.Name);

        await Assert.That(events.Count).IsGreaterThanOrEqualTo(eventCount);
        foreach (var evt in events) {
            await Assert.That(evt).IsNotNull();
        }
    }

    [Test]
    public async Task EventGeneration_ForSpecificDevices_ShouldGenerateValidEvents() {
        // Arrange
        var faker = new Faker();
        var devices = faker.HomeAutomation().Devices(count: 3);
        const int eventCount = 8;

        // Act
        var events = faker.HomeAutomation().Events(devices, count: eventCount);

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
