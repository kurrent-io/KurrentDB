namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Common device information shared across all IoT events.
/// </summary>
public record DeviceInfo {
    public required string     Id               { get; init; }
    public required string     Name             { get; init; }
    public required Guid       RoomId           { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }
    public          int?       BatteryLevel     { get; init; }
}

public record TemperatureReading {
    public          Guid            EventId     { get; init; } = Guid.NewGuid();
    public required DeviceInfo      Device      { get; init; }
    public required double          Temperature { get; init; }
    public          TemperatureUnit Unit        { get; init; } = TemperatureUnit.Celsius;
    public required long            Timestamp   { get; init; }
}

public record MotionDetected {
    public          Guid       EventId    { get; init; } = Guid.NewGuid();
    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}

public record DoorStateChanged {
    public          Guid       EventId    { get; init; } = Guid.NewGuid();
    public required DeviceInfo Device     { get; init; }
    public required bool       IsOpen     { get; init; }
    public required LockStatus LockStatus { get; init; }
    public          string     UserId     { get; init; } = "";
    public required long       Timestamp  { get; init; }
}

public record WindowStateChanged {
    public          Guid             EventId          { get; init; } = Guid.NewGuid();
    public required DeviceInfo       Device           { get; init; }
    public required bool             IsOpen           { get; init; }
    public required int              OpenPercentage   { get; init; }
    public required WeatherCondition WeatherCondition { get; init; }
    public required long             Timestamp        { get; init; }
}

public record SmokeDetected {
    public          Guid        EventId     { get; init; } = Guid.NewGuid();
    public required DeviceInfo  Device      { get; init; }
    public required bool        HasSmoke    { get; init; }
    public required int         SmokeLevel  { get; init; }
    public required AlarmStatus AlarmStatus { get; init; }
    public required long        Timestamp   { get; init; }
}

public record WaterLeakDetected {
    public          Guid       EventId       { get; init; } = Guid.NewGuid();
    public required DeviceInfo Device        { get; init; }
    public required bool       HasWater      { get; init; }
    public required int        MoistureLevel { get; init; }
    public required bool       AlertSent     { get; init; }
    public required long       Timestamp     { get; init; }
}

public record LightStateChanged {
    public          Guid       EventId          { get; init; } = Guid.NewGuid();
    public required DeviceInfo Device           { get; init; }
    public required bool       IsOn             { get; init; }
    public required int        Brightness       { get; init; }
    public          string     Color            { get; init; } = "";
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}

public record HumidityReading {
    public          Guid         EventId      { get; init; } = Guid.NewGuid();
    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}
