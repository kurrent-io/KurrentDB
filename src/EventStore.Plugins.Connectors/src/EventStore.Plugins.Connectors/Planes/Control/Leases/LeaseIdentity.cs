using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DotNext.Threading.Leases;

/// <summary>
/// Represents a lease in the particular point in time.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct LeaseIdentity : IEquatable<LeaseIdentity>
{
    /// <summary>
    /// Represents initial version of a lease which cannot be renewed or released but can be acquired.
    /// </summary>
    [CLSCompliant(false)]
    public const ulong InitialVersion = 0UL;

    /// <summary>
    /// Gets a version of the lease.
    /// </summary>
    [CLSCompliant(false)]
    public required ulong Version { get; init; }

    /// <summary>
    /// Gets an ID of the lease.
    /// </summary>
    /// <remarks>
    /// This property can be used only if the provider supports the deletion of leases.
    /// In that case, a newly created lease after its deletion must have unique random ID
    /// to prevent its renewal from the stale client.
    /// </remarks>
    public Guid Id { get; init; }

    private bool Equals(in LeaseIdentity other)
        => Version == other.Version && Id == other.Id;

    /// <summary>
    /// Determines whether this identity is the same as the specified one.
    /// </summary>
    /// <param name="other">The identity to be compared.</param>
    /// <returns><see langword="true"/> if this identity is the same as <paramref name="other"/>; otherwise, <see langword="false"/>.</returns>
    public bool Equals(LeaseIdentity other) => Equals(in other);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? other)
        => other is LeaseIdentity identity && Equals(in identity);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Version, Id);

    /// <summary>
    /// Determines whether the two identities are equal.
    /// </summary>
    /// <param name="x">The first identity to compare.</param>
    /// <param name="y">The second identity to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="x"/> is equal to <paramref name="y"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(in LeaseIdentity x, in LeaseIdentity y)
        => x.Equals(in y);

    /// <summary>
    /// Determines whether the two identities are not equal.
    /// </summary>
    /// <param name="x">The first identity to compare.</param>
    /// <param name="y">The second identity to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="x"/> is not equal to <paramref name="y"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(in LeaseIdentity x, in LeaseIdentity y)
        => x.Equals(in y) is false;

    internal LeaseIdentity BumpVersion() => this with { Version = Version + 1UL };
}