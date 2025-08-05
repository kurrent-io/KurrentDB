// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace KurrentDB.Surge.Testing.VehicleRegistration;

public enum EngineType {
    Undefined = 0,
    Petrol    = 1,
    Diesel    = 2,
    Hybrid    = 3,
    Electric  = 4
}

public record Vehicle {
    public string     Make       { get; set; } // Manufacturer: Ford, Toyota, Honda, etc.
    public string     Model      { get; set; } // Model: F150, Camry, Civic, etc.
    public int        Year       { get; set; } // Year of manufacture
    public EngineType EngineType { get; set; } // Engine type: Petrol, Diesel, Hybrid, Electric
}

public record Owner {
    public string   DocumentId  { get; set; }
    public string   Name        { get; set; }
    public int      Age         { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string   FirstName   { get; set; }
    public string   LastName    { get; set; }
    public string   PhoneNumber { get; set; }
    public string   Email       { get; set; }
}

public record VehicleRegistration {
    public Guid     RegistrationId     { get; set; }
    public long     RegistrationNumber { get; set; }
    public Vehicle  Vehicle            { get; set; }
    public string   Vin                { get; set; }
    public Owner    Owner              { get; set; }
    public string   Color              { get; set; }
    public DateTime RegistrationDate   { get; set; }
}
