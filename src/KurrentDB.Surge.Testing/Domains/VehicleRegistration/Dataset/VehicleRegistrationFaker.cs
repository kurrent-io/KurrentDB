// ReSharper disable CheckNamespace
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Bogus.DataSet.Vehicles;

public record Owner {
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

public class VehicleRegistrationFaker : Faker<VehicleRegistration> {
    public static readonly VehicleRegistrationFaker Instance = new();

    public VehicleRegistrationFaker(string? locale = null) : base(locale ?? "en", null) {
        RuleFor(x => x.RegistrationId, _ => Guid.NewGuid());
        RuleFor(x => x.RegistrationNumber, f => f.Random.Long(1000000000, 9999999999));
        RuleFor(x => x.RegistrationDate, f => f.Date.Past(3));
        RuleFor(x => x.Vin, f => f.Vehicle.Vin());
        RuleFor(x => x.Vehicle, f => f.PickRandom(VehicleDataset.Data));
        RuleFor(x => x.Color, f => f.Commerce.Color());
        RuleFor(x => x.Owner, _ => GenerateOwner());
    }

    Owner GenerateOwner() {
        var person = FakerHub.Person;

        var owner = new Owner {
            Name        = person.FullName,
            FirstName   = person.FirstName,
            LastName    = person.LastName,
            DateOfBirth = person.DateOfBirth,
            Age         = CalculateAge(person.DateOfBirth),
            PhoneNumber = person.Phone,
            Email       = person.Email,
        };

        return owner;

        static int CalculateAge(DateTime dateOfBirth) {
            var today = DateTime.Today;
            var age   = today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}

public class VehicleRegisteredFaker : Faker<VehicleRegistered> {
    public static readonly VehicleRegisteredFaker Instance = new();

    public VehicleRegisteredFaker(string? locale = null) : base(locale ?? "en", null) {
        RuleFor(x => x.RegistrationId, _ => Guid.NewGuid());
        RuleFor(x => x.RegistrationNumber, f => f.Random.Long(1000000000, 9999999999));
        RuleFor(x => x.RegistrationDate, f => f.Date.Past(3));
        RuleFor(x => x.Vin, f => f.Vehicle.Vin());
        RuleFor(x => x.Vehicle, f => f.PickRandom(VehicleDataset.Data));
        RuleFor(x => x.Color, f => f.Commerce.Color());
        RuleFor(x => x.Owner, _ => GenerateOwner());
    }

    Owner GenerateOwner() {
        var person = FakerHub.Person;

        var owner = new Owner {
            Name        = person.FullName,
            FirstName   = person.FirstName,
            LastName    = person.LastName,
            DateOfBirth = person.DateOfBirth,
            Age         = CalculateAge(person.DateOfBirth),
            PhoneNumber = person.Phone,
            Email       = person.Email,
        };

        return owner;

        static int CalculateAge(DateTime dateOfBirth) {
            var today = DateTime.Today;
            var age   = today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}

public record VehicleRegistered {
    public Guid     RegistrationId     { get; set; }
    public long     RegistrationNumber { get; set; }
    public Vehicle  Vehicle            { get; set; }
    public string   Vin                { get; set; }
    public Owner    Owner              { get; set; }
    public string   Color              { get; set; }
    public DateTime RegistrationDate   { get; set; }
}