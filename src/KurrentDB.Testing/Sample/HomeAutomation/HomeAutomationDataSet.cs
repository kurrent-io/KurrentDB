using Bogus;

namespace KurrentDB.Testing.Sample.HomeAutomation;

public partial class HomeAutomationDataSet : DataSet {
    public static readonly HomeAutomationDataSet Default = new();

    public HomeAutomationDataSet(string locale = "en") : base(locale) =>
        Faker = new Faker(locale);

    public HomeAutomationDataSet(Faker faker) : base(faker.Locale) =>
        Faker = faker;

    Faker Faker { get; }

    /// <summary>
    /// Generate a random home with optional configuration parameters
    /// </summary>
    public Home Home(int? rooms = null, int? devicesPerRoom = null, params DeviceType[] deviceTypes) {
        rooms          ??= Faker.Random.Number(2, 4);
        devicesPerRoom ??= Faker.Random.Number(3, 7);

        return GenerateCustomHome(rooms, devicesPerRoom, deviceTypes);
    }

    public List<Home> Homes(int? count = null, int? averageNumberOfRooms = null, int? averageNumberOfDevicesPerRoom = null, params DeviceType[] deviceTypes) {
        count ??= Faker.Random.Number(3, 7);

        averageNumberOfRooms = averageNumberOfRooms is null
            ? Faker.Random.Number(2, 4)
            : Faker.Random.Number(Math.Max(1, averageNumberOfRooms.Value - 2), averageNumberOfRooms.Value + 2);

        averageNumberOfDevicesPerRoom = averageNumberOfDevicesPerRoom is null
            ? Faker.Random.Number(3, 7)
            : Faker.Random.Number(Math.Max(1, averageNumberOfDevicesPerRoom.Value - 2), averageNumberOfDevicesPerRoom.Value + 2);

        return Enumerable.Range(0, count.Value)
            .Select(_ => GenerateCustomHome(averageNumberOfRooms, averageNumberOfDevicesPerRoom, deviceTypes))
            .ToList();
    }

    /// <summary>
    /// Generate a collection of devices with optional parameters
    /// </summary>
    public List<Device> Devices(int? count = null, DeviceType[]? types = null, List<Room>? rooms = null) {
        // Use provided rooms or generate default ones
        var targetRooms = rooms ?? GenerateDefaultRooms();
        var deviceCount = count ?? Faker.Random.Number(5, 15);

        var tempHome = new Home {
            Id        = Faker.Random.AlphaNumeric(6).ToLower(),
            Name      = "Temp Home",
            Rooms     = targetRooms,
            Devices   = [], // Empty for now
            CreatedAt = DateTime.UtcNow
        };

        if (types is null || types.Length == 0) {
            // Use standard device collection faker
            var deviceFaker = new HomeDeviceCollectionFaker(tempHome, deviceCount);
            return deviceFaker.Generate();
        }

        // Generate devices with specific types
        return GenerateTypedDevices(targetRooms, deviceCount, types);

        List<Room> GenerateDefaultRooms() {
            var roomFaker = new RoomFaker();
            return new List<Room> {
                new(Guid.NewGuid(), "Living Room", RoomType.LivingRoom),
                new(Guid.NewGuid(), "Kitchen", RoomType.Kitchen),
                new(Guid.NewGuid(), "Master Bedroom", RoomType.Bedroom)
            }.Concat(roomFaker.Generate(Faker.Random.Number(2, 4))).ToList();
        }
    }

    /// <summary>
    /// Generate devices specifically for given rooms
    /// </summary>
    public List<Device> Devices(List<Room> rooms, int? devicesPerRoom = null) {
        var devicesPerRoomCount = devicesPerRoom ?? Faker.Random.Number(1, 3);
        var totalDevices        = rooms.Count * devicesPerRoomCount;

        return Devices(totalDevices, rooms: rooms);
    }

    /// <summary>
    /// Generate events for specific devices
    /// </summary>
    public List<object> Events(List<Device> devices, int count, long? startTime = null, int maxIntervalMinutes = 30) {
        if (devices.Count == 0) return [];

        var events = new List<object>();
        var timestamp = startTime ?? DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        for (var i = 0; i < count; i++) {
            timestamp += Faker.Random.Number(1, maxIntervalMinutes) * 60000L;

            var device   = Faker.PickRandom(devices);
            var eventObj = GenerateEventForDevice(device, timestamp);

            if (eventObj != null) {
                events.Add(eventObj);

                // Generate correlated events
                var correlatedEvents = GenerateCorrelatedEvents(device, timestamp, devices, Faker);
                events.AddRange(correlatedEvents);
            }
        }

        return events;

        static object? GenerateEventForDevice(Device device, long timestamp) {
            return device switch {
                Thermostat thermostat         => new TemperatureReadingFaker(thermostat, timestamp).Generate(),
                MotionSensor motionSensor     => new MotionDetectedFaker(motionSensor, timestamp).Generate(),
                SmartLight smartLight         => new LightStateChangedFaker(smartLight, timestamp).Generate(),
                DoorLock doorLock             => new DoorStateChangedFaker(doorLock, timestamp).Generate(),
                SmokeDetector smokeDetector   => new SmokeDetectedFaker(smokeDetector, timestamp).Generate(),
                HumiditySensor humiditySensor => new HumidityReadingFaker(humiditySensor, timestamp).Generate(),
                WindowSensor windowSensor     => new WindowStateChangedFaker(windowSensor, timestamp).Generate(),
                WaterSensor waterSensor       => new WaterLeakDetectedFaker(waterSensor, timestamp).Generate(),
                _                             => null // Skip unsupported device types
            };
        }

        static IEnumerable<object> GenerateCorrelatedEvents(Device device, long timestamp, List<Device> availableDevices, Faker faker) {
            // Motion sensor correlation: trigger lights in the same room
            if (device is MotionSensor motionSensor && faker.Random.Bool(0.3f)) {
                var lightsInSameRoom = availableDevices
                    .OfType<SmartLight>()
                    .Where(light => light.Room.RoomId == motionSensor.Room.RoomId)
                    .Take(1);

                foreach (var light in lightsInSameRoom) {
                    var correlatedTimestamp = timestamp + faker.Random.Long(2000L, 10000L);
                    yield return new LightStateChangedFaker(light, correlatedTimestamp).Generate();
                }
            }
        }
    }

    /// <summary>
    /// Generate events for a home's devices
    /// </summary>
    public List<object> Events(Home home, int count, long? startTime = null, int maxIntervalMinutes = 30) =>
        Events(home.Devices, count, startTime, maxIntervalMinutes);

    /// <summary>
    /// Generate random events (creates random devices first)
    /// </summary>
    public List<object> Events(int count, long? startTime = null, int maxIntervalMinutes = 30) {
        var randomDevices = Devices(Faker.Random.Number(3, 8));
        return Events(randomDevices, count, startTime, maxIntervalMinutes);
    }

    Home GenerateCustomHome(int? rooms, int? devicesPerRoom, params DeviceType[] deviceTypes) {
        var roomCount          = rooms ?? Faker.Random.Number(3, 8);
        var deviceCountPerRoom = devicesPerRoom ?? Faker.Random.Number(1, 3);

        // Generate home properties
        var homeId = Faker.Random.AlphaNumeric(6).ToLower();
        var homeName = Faker.PickRandom(
            $"{Faker.Name.LastName()} Family Home",
            $"{Faker.Name.LastName()} Residence",
            $"{Faker.Address.StreetName()} House",
            $"{Faker.Address.StreetName()} Villa",
            $"{Faker.Address.City()} Apartment {Faker.Random.Number(1, 50)}"
        );

        // Generate rooms
        var roomFaker = new RoomFaker();
        var homeRooms = roomFaker.Generate(roomCount);

        // Generate devices
        var totalDevices = roomCount * deviceCountPerRoom;
        var devices = deviceTypes.Length == 0
            ? Devices(totalDevices, rooms: homeRooms)
            : GenerateTypedDevices(homeRooms, totalDevices, deviceTypes);

        return new Home {
            Id        = homeId,
            Name      = homeName,
            Rooms     = homeRooms,
            Devices   = devices,
            CreatedAt = Faker.Date.Between(DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddMonths(-1))
        };
    }



    List<Device> GenerateTypedDevices(List<Room> rooms, int totalDevices, DeviceType[] allowedTypes) {
        var devices = new List<Device>();
        var homeId  = Faker.Random.AlphaNumeric(6).ToLower();

        for (var i = 0; i < totalDevices; i++) {
            var room       = Faker.PickRandom(rooms);
            var deviceType = Faker.PickRandom(allowedTypes);

            var deviceFaker = new DeviceFaker(room, deviceType, homeId);
            devices.Add(deviceFaker.Generate());
        }

        return devices;
    }
}
