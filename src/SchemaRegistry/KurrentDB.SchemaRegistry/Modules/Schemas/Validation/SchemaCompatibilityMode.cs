namespace Kurrent.Surge.Schema.Validation;

public enum SchemaCompatibilityMode {
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

	// BACKWARD_ALL allows data receivers to read both the current and all
	// previous schema versions. You can use this choice when you need to
	// delete fields or add optional fields, and check compatibility
	// against all previous schema versions.
	BackwardAll = 4,

	// FORWARD_ALL allows data receivers to read written by producers of any
	// new registered schema. You can use this choice when you need to add
	// fields or delete optional fields, and check compatibility against
	// all previous schema versions.
	ForwardAll = 5,

	// FULL ALL compatibility allows data receivers to read data written by
	// producers using all previous schema versions. You can use this choice when
	// you need to add or remove optional fields, and check compatibility against
	// all previous schema versions.
	FullAll = 6,

	// NONE disables compatibility checks, allowing any kind of schema change.
	// This mode should be used with caution, as it may lead to compatibility
	// issues.
	None = 7,
}
