// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace KurrentDB.Surge.Testing.VehicleRegistration;

public record VehicleRegistered {
    public Guid     RegistrationId     { get; set; }
    public long     RegistrationNumber { get; set; }
    public Vehicle  Vehicle            { get; set; }
    public string   Vin                { get; set; }
    public Owner    Owner              { get; set; }
    public string   Color              { get; set; }
    public DateTime RegistrationDate   { get; set; }

    public override string ToString() =>
        $"Vehicle {Vehicle.Make} {Vehicle.Model} ({Vehicle.Year}) registered to {Owner.FirstName} {Owner.LastName} on {RegistrationDate:d}.";
}

public record VehicleDeregistered {
    public Guid     RegistrationId     { get; set; }
    public DateTime DeregistrationDate { get; set; }
    public string   Reason             { get; set; }

    public override string ToString() => $"Vehicle with Registration ID {RegistrationId} was deregistered on {DeregistrationDate:d} due to '{Reason}'.";
}

public record OwnershipTransferred {
    public Guid     RegistrationId { get; set; }
    public Owner    PreviousOwner  { get; set; }
    public Owner    NewOwner       { get; set; }
    public DateTime TransferDate   { get; set; }

    public override string ToString() =>
        $"Ownership of vehicle with Registration ID {RegistrationId} transferred from {PreviousOwner.FirstName} {PreviousOwner.LastName} to {NewOwner.FirstName} {NewOwner.LastName} on {TransferDate:d}.";
}

public record RegistrationRenewed {
    public Guid     RegistrationId { get; set; }
    public DateTime RenewalDate    { get; set; }
    public DateTime NewExpiryDate  { get; set; }

    public override string ToString() =>
        $"Registration for vehicle with Registration ID {RegistrationId} renewed on {RenewalDate:d}, new expiry date is {NewExpiryDate:d}.";
}

public record VehicleInspectionPassed {
    public Guid     InspectionId   { get; set; }
    public Vehicle  Vehicle        { get; set; }
    public DateTime InspectionDate { get; set; }
    public string   InspectorName  { get; set; }

    public override string ToString() => $"Vehicle {Vehicle.Make} {Vehicle.Model} ({Vehicle.Year}) passed inspection on {InspectionDate:d} by {InspectorName}.";
}

public record VehicleInspectionFailed {
    public Guid     InspectionId   { get; set; }
    public Vehicle  Vehicle        { get; set; }
    public DateTime InspectionDate { get; set; }
    public string   InspectorName  { get; set; }
    public string   FailureReason  { get; set; }

    public override string ToString() =>
        $"Vehicle {Vehicle.Make} {Vehicle.Model} ({Vehicle.Year}) failed inspection on {InspectionDate:d} by {InspectorName}. Reason: {FailureReason}.";
}

public record OwnerDetailsUpdated {
    public Guid     OwnerId             { get; set; }
    public Owner    UpdatedOwnerDetails { get; set; }
    public DateTime UpdateDate          { get; set; }

    public override string ToString() =>
        $"Owner details for ID {OwnerId} updated on {UpdateDate:d} to {UpdatedOwnerDetails.FirstName} {UpdatedOwnerDetails.LastName}.";
}

public record VehicleStolenReported {
    public Guid     RegistrationId { get; set; }
    public DateTime ReportDate     { get; set; }
    public string   ReportedBy     { get; set; }

    public override string ToString() => $"Vehicle with Registration ID {RegistrationId} reported stolen by {ReportedBy} on {ReportDate:d}.";
}

public record VehicleRecoveredReported {
    public Guid     RegistrationId { get; set; }
    public DateTime RecoveryDate   { get; set; }
    public string   RecoveredBy    { get; set; }

    public override string ToString() => $"Vehicle with Registration ID {RegistrationId} reported recovered by {RecoveredBy} on {RecoveryDate:d}.";
}
