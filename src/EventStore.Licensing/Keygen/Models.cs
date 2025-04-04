// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Licensing.Keygen;

public static class Models {
	public record ValidateLicenseRequest(ValidateLicenseRequest.RequestMeta Meta) {
		public record RequestMeta(string? Key, FingerprintScope Scope);
		public record FingerprintScope(string Fingerprint);
	}

	// Is a given license currently valid for use with a given machine?
	public class ValidateLicenseResponse : KeygenResponse<LicenseAttributes, LicenseValidationResult, LicenseRelationships> {
		public LicenseStatus GetStatus() => Meta switch {
			// TOO_MANY_MACHINES/CORES/PROCESSES can come back here, but in those cases all we care about
			// is Valid or not which is determined by the overage strategy on the policy.
			{ Valid: false, Code: "NO_MACHINES" } => LicenseStatus.InvalidNoMachines,
			{ Valid: false, Code: "FINGERPRINT_SCOPE_MISMATCH" } => LicenseStatus.InvalidMachineMismatch,
			{ Valid: false, Code: "HEARTBEAT_NOT_STARTED" } => LicenseStatus.InvalidHeartbeatNotStarted,
			{ Valid: false, Code: "HEARTBEAT_DEAD" } => LicenseStatus.InvalidHeartbeatDead,
			{ Valid: false } => LicenseStatus.InvalidOther,
			{ Valid: true } => LicenseStatus.Valid,
			_ => throw new ArgumentOutOfRangeException()
		};
	}

	// this is the status of a license with respect to a machine
	public enum LicenseStatus {
		Unknown = 0,

		// no machines associated with the license. Need to activate.
		InvalidNoMachines,

		// machines are associated with the license, but not this machine. Need to activate.
		InvalidMachineMismatch,

		// this machine is associated with the license but never heartbeated. Need to deactivate.
		InvalidHeartbeatNotStarted,

		// this machine is associated with the license but has stopped heartbeating. Need to deactivate.
		InvalidHeartbeatDead,

		// invalid but no action we can take automatically to recover.
		InvalidOther,

		Valid,
	}

	public class LicenseAttributes : KeygenAttributes {
		public string Key { get; set; } = null!;
		public DateTimeOffset? Expiry { get; set; }
		public string Status { get; set; } = null!;
		public bool Suspended { get; set; } = true;
		public int MaxMachines { get; set; }
		public int? MaxCores { get; set; }
		public bool RequiresHeartbeat { get; set; }
	}

	public class LicenseValidationResult {
		public DateTimeOffset Ts { get; set; }
		public bool Valid { get; set; }
		public string Detail { get; set; } = null!;
		public string Code { get; set; } = null!;
	}

	public class LicenseRelationships {
		public LinkData Product { get; set; } = null!;
		public LinkData Policy { get; set; } = null!;
	}

	public class ActivateMachineResponse : KeygenResponse<MachineAttributes, object, object> {
	}

	public class HeartbeatResponse : KeygenResponse<MachineAttributes, object, object> {
		public string HeartbeatStatus => Data?.Attributes.HeartbeatStatus ?? "UNKNOWN";
	}

	public class GetMachineResponse : KeygenResponse<MachineAttributes, object, object> {
		public bool RequiresHeartbeat => Data?.Attributes.RequireHeartbeat == true;
		public int HeartbeatInterval => Data?.Attributes.HeartbeatDuration ?? 0;
	}

	public class MachineAttributes : KeygenAttributes {
		public bool RequireHeartbeat { get; set; }
		public int HeartbeatDuration { get; set; }
		public string HeartbeatStatus { get; set; } = null!;
	}

	public class EntitlementsResponse : KeygenListResponse<EntitlementAttributes, object, object>;

	public class EntitlementAttributes : KeygenAttributes {
		public string Code { get; set; } = null!;
	}

	public record Link(string Type, string Id);

	public record LinkData(Link Data);

	public class KeygenData<TAttributes, TRelationships> where TAttributes : KeygenAttributes where TRelationships : class {
		public string Id { get; set; } = null!;
		public TAttributes Attributes { get; set; } = null!;
		public TRelationships? Relationships { get; init; }
	}

	public abstract class KeygenResponse {
		abstract public bool IsSuccess { get; }
		public KeygenError[] Errors { get; set; } = [];
	}

	public class KeygenResponse<TAttributes, TMeta, TRelationships> : KeygenResponse
		where TAttributes : KeygenAttributes
		where TMeta : class
		where TRelationships : class {

		public override bool IsSuccess => Data is not null;
		public KeygenData<TAttributes, TRelationships>? Data { get; set; }
		public TMeta? Meta { get; init; }
	}

	public class KeygenListResponse<TAttributes, TMeta, TRelationships> : KeygenResponse
		where TAttributes : KeygenAttributes
		where TMeta : class
		where TRelationships : class {

		public override bool IsSuccess => Data is not null;
		public KeygenData<TAttributes, TRelationships>[]? Data { get; set; }
		public TMeta? Meta { get; init; }
	}

	public record KeygenError {
		public string Title { get; set; } = null!;
		public string Detail { get; set; } = null!;
		public string Code { get; set; } = null!;
	}

	public class KeygenAttributes {
		public string Name { get; set; } = null!;
		public Dictionary<string, object> Metadata { get; set; } = null!;
	}
}
