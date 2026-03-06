namespace KurrentDB.Api.Streams;

/// <summary>
/// The expected state of a stream. Not a concrete revision.
/// Superset of <see cref="ActualStreamCondition"/> — includes the expectation-only values Any and Exists.
/// </summary>
public static class ExpectedStreamCondition {
	/// <summary>
	/// The stream must not exist.
	/// </summary>
	/// <remarks>
	/// The operation fails if the stream already has any committed records.
	/// <para />
	/// Use this to guarantee that the operation creates a new stream rather than appending to an existing one.
	/// </remarks>
	public const long NoStream = -1;

	/// <summary>
	/// No precondition on the stream's existence or revision.
	/// </summary>
	/// <remarks>
	/// The operation succeeds regardless of whether the stream exists, has been deleted, or is at any particular revision.
	/// <para />
	/// Use this when you do not need optimistic concurrency and simply want the operation to proceed unconditionally.
	/// </remarks>
	public const long Any = -2;

	/// <summary>
	/// The stream must already exist with at least one committed record.
	/// </summary>
	/// <remarks>
	/// The operation fails if the stream has never been written to or is in the NoStream state.
	/// <para />
	/// Unlike specifying a concrete revision, this does not check the exact revision —
	/// only that the stream is non-empty.
	/// </remarks>
	public const long Exists = -4;

	/// <summary>
	/// The stream must be in a soft-deleted state.
	/// </summary>
	/// <remarks>
	/// The operation fails if the stream has not been soft-deleted.
	/// <para />
	/// A soft-deleted stream can be recreated; its revision is preserved and continues to increment for new records.
	/// </remarks>
	public const long Deleted = -5;

	/// <summary>
	/// The stream must be in a tombstoned state.
	/// </summary>
	/// <remarks>
	/// The operation fails if the stream has not been hard-deleted and tombstoned.
	/// <para />
	/// A tombstoned stream cannot be recreated or have any new records appended to it.
	/// </remarks>
	public const long Tombstoned = -6;
}

/// <summary>
/// The actual state of a stream. Not a concrete revision.
/// </summary>
public static class ActualStreamCondition {
	/// <summary>
	/// The stream does not exist.
	/// </summary>
	/// <remarks>
	/// The stream has never been created or appended to, or it has been soft-deleted and then recreated, resulting in a positive revision number.
	/// </remarks>
	public const long NotFound = -1;

	/// <summary>
	/// The stream has been soft-deleted.
	/// </summary>
	/// <remarks>
	/// All records have been removed from the stream, however the stream may still be recreated with the same name and new records appended to it.
	/// <para />
	/// Its revision was not reset and will continue to increment for new records.
	/// </remarks>
	public const long Deleted = -5;

	/// <summary>
	/// The stream has been hard-deleted and tombstoned.
	/// </summary>
	/// <remarks>
	/// All records have been removed from the stream and it is no longer accessible.
	/// <para />
	/// It cannot be recreated or have any new records appended to it.
	/// </remarks>
	public const long Tombstoned = -6;
}
