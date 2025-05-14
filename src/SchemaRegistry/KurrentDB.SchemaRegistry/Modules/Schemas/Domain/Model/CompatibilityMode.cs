namespace KurrentDB.SchemaRegistry.Domain;

public enum CompatibilityMode {
    // Default value and should not be used
    Unspecified = 0,

    // BACKWARD compatibility ensures that new schema versions can be read by
    // clients using older schema versions. This allows for schema evolution with
    // the addition of new optional fields or types.
    Backward = 1,

    // FORWARD compatibility ensures that new clients can read data produced with
    // older schema versions. This allows for schema evolution with the removal
    // of fields or types, but not the addition of required fields.
    Forward = 2,

    // FULL compatibility is the strictest mode, ensuring both backward and
    // forward compatibility. New schema versions must be fully compatible with
    // older versions, allowing only safe changes like adding optional fields or
    // types.
    Full = 3,

    // NONE disables compatibility checks, allowing any kind of schema change.
    // This mode should be used with caution, as it may lead to compatibility
    // issues.
    None = 4
}