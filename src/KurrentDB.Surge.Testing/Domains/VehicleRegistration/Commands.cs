// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace KurrentDB.Surge.Testing.VehicleRegistration;

public record RegisterVehicle {
    public Vehicle Vehicle { get; set; }
    public Owner   Owner   { get; set; }
    public string  Color   { get; set; }

    public override string ToString() =>
        $"Register vehicle {Vehicle.Make} {Vehicle.Model} ({Vehicle.Year}), owned by {Owner.FirstName} {Owner.LastName}, color {Color}.";
}

public record TransferVehicleOwnership {
    public Guid  RegistrationId { get; set; }
    public Owner NewOwner       { get; set; }

    public override string ToString() => $"Transfer ownership of vehicle with Registration ID {RegistrationId} to {NewOwner.FirstName} {NewOwner.LastName}.";
}

public record DeregisterVehicle {
    public Guid   RegistrationId { get; set; }
    public string Reason         { get; set; }

    public override string ToString() => $"Deregister vehicle with Registration ID {RegistrationId} due to '{Reason}'.";
}

public record RenewVehicleRegistration {
    public Guid RegistrationId { get; set; }

    public override string ToString() => $"Renew registration for vehicle with Registration ID {RegistrationId}.";
}

public record UpdateOwnerDetails {
    public Guid  OwnerId { get; set; }
    public Owner Details { get; set; }

    public override string ToString() => $"Update details for owner ID {OwnerId} to {Details.FirstName} {Details.LastName}.";
}

public record ReportVehicleStolen {
    public Guid   RegistrationId { get; set; }
    public string ReportedBy     { get; set; }

    public override string ToString() => $"Report vehicle with Registration ID {RegistrationId} as stolen by {ReportedBy}.";
}

public record ReportVehicleRecovered {
    public Guid   RegistrationId { get; set; }
    public string RecoveredBy    { get; set; }

    public override string ToString() => $"Report vehicle with Registration ID {RegistrationId} as recovered by {RecoveredBy}.";
}
