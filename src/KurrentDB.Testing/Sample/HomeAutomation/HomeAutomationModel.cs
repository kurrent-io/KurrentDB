using Humanizer;

namespace KurrentDB.Testing.Sample.HomeAutomation;

public enum RoomType {
    Unspecified = 0,
    LivingRoom,
    Kitchen,
    Bedroom,
    Bathroom,
    Office,
    Garage,
    Hallway,
    DiningRoom,
    Basement,
    Utility,
    Entrance
}

public enum DeviceType {
    Unspecified = 0,
    Thermostat,
    MotionSensor,
    SmartLight,
    DoorLock,
    SmokeDetector,
    HumiditySensor,
    WindowSensor,
    WaterSensor
}

public enum TemperatureUnit {
    Unspecified = 0,
    Celsius,
    Fahrenheit,
    Kelvin
}

public enum AlarmStatus {
    Unspecified = 0,
    Normal,
    Active,
    Testing,
    Maintenance
}

public enum LockStatus {
    Unspecified = 0,
    Locked,
    Unlocked,
    Unknown,
    Jammed
}

public enum WeatherCondition {
    Unspecified = 0,
    Sunny,
    Cloudy,
    Rainy,
    Windy,
    Stormy
}

public enum ComfortLevel {
    Unspecified = 0,
    Low,
    Optimal,
    High
}

/// <summary>
/// Represents a room within a home
/// </summary>
public class Room {
    public Room() { }

    public Room(Guid roomId, string name, RoomType type) {
        RoomId = roomId;
        Name   = name;
        Type   = type;
    }

    public Guid     RoomId { get; set; } = Guid.Empty;
    public string   Name   { get; set; } = "";
    public RoomType Type   { get; set; } = RoomType.Unspecified;
}

/// <summary>
/// Represents a home with rooms
/// </summary>
public class Home {
    public string       Id        { get; set; } = "";
    public string       Name      { get; set; } = "";
    public List<Room>   Rooms     { get; set; } = [];
    public List<Device> Devices   { get; set; } = [];
    public DateTime     CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Base class for all IoT devices
/// </summary>
public abstract class Device(Room room, DeviceType deviceType, bool isBatteryPowered, string deviceId) {
    public string     Id               { get; init; } = deviceId;
    public string     FriendlyName     { get; init; } = $"{room.Name} {deviceType.Humanize()}";
    public Room       Room             { get; init; } = room;
    public DeviceType DeviceType       { get; init; } = deviceType;
    public bool       IsBatteryPowered { get; init; } = isBatteryPowered;

    public virtual DeviceInfo CreateDeviceInfo(int? batteryLevel = null) =>
        new() {
            Id               = Id,
            Name             = FriendlyName,
            RoomId           = Room.RoomId,
            DeviceType       = DeviceType,
            IsBatteryPowered = IsBatteryPowered,
            BatteryLevel     = IsBatteryPowered ? batteryLevel : null
        };
}

public class Thermostat(Room room, string deviceId)
    : Device(room, DeviceType.Thermostat, true, deviceId) {
    public int    BatteryLevel       { get; set; } = 85;
    public double CurrentTemperature { get; set; } = 22.0;

    public void UpdateTemperature(double newTemperature) =>
        CurrentTemperature = Math.Max(5.0, Math.Min(45.0, newTemperature));

    public void DepleteBattery(int amount = 1) =>
        BatteryLevel = Math.Max(5, BatteryLevel - amount);
}

public class MotionSensor(Room room, string deviceId)
    : Device(room, DeviceType.MotionSensor, false, deviceId) {
    public long LastMotionTime { get; set; } = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();

    public void RecordMotion(long timestamp) => LastMotionTime = timestamp;
}

public class SmartLight(Room room, string deviceId) : Device(room, DeviceType.SmartLight, false, deviceId ) {
    public bool IsOn       { get; set; }
    public int  Brightness { get; set; }

    public void ToggleLight() {
        IsOn = !IsOn;
        if (!IsOn) Brightness = 0;
    }

    public void SetBrightness(int brightness) {
        if (IsOn) Brightness = Math.Max(0, Math.Min(100, brightness));
    }
}

public class DoorLock(Room room, string deviceId)
    : Device(room, DeviceType.DoorLock, false, deviceId ) {
    public bool       IsOpen     { get; set; }
    public LockStatus LockStatus { get; set; } = LockStatus.Locked;

    public void ToggleDoor() {
        IsOpen = !IsOpen;
        if (IsOpen) LockStatus = LockStatus.Unlocked;
    }

    public void SetLockStatus(LockStatus status) {
        LockStatus = status;
        if (status == LockStatus.Locked) IsOpen = false;
    }
}

public class SmokeDetector(Room room, string deviceId)
    : Device(room, DeviceType.SmokeDetector, true, deviceId) {
    public int BatteryLevel { get; set; } = 90;

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}

public class HumiditySensor(Room room, string deviceId)
    : Device(room, DeviceType.HumiditySensor, true, deviceId) {
    public int    BatteryLevel  { get; set; } = 85;
    public double HumidityLevel { get; set; } = 45.0; // Starting at 45% humidity

    public void UpdateHumidity(double newHumidity) =>
        HumidityLevel = Math.Max(0.0, Math.Min(100.0, newHumidity));

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}

public class WindowSensor(Room room, string deviceId)
    : Device(room, DeviceType.WindowSensor, true, deviceId) {
    public int  BatteryLevel  { get; set; } = 90;
    public bool IsOpen        { get; set; } = false;
    public long LastOpenTime  { get; set; } = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

    public void ToggleWindow() {
        IsOpen = !IsOpen;
        if (IsOpen) RecordOpening(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void RecordOpening(long timestamp) => LastOpenTime = timestamp;

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}

public class WaterSensor(Room room, string deviceId)
    : Device(room, DeviceType.WaterSensor, true, deviceId) {
    public int  BatteryLevel  { get; set; } = 95;
    public bool WaterDetected { get; set; } = false;
    public int  WaterLevel    { get; set; } = 0; // 0-100 scale

    public void DetectWater(bool hasWater = true) {
        WaterDetected = hasWater;
        if (!hasWater) WaterLevel = 0;
    }

    public void UpdateWaterLevel(int level) {
        WaterLevel = Math.Max(0, Math.Min(100, level));
        WaterDetected = WaterLevel > 0;
    }

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}
