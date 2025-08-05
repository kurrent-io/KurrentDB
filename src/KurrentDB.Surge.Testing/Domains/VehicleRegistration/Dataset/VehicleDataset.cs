// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable UnusedMember.Global
// ReSharper disable ClassNeverInstantiated.Global

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

using System.Text.Json;
using System.Text.Json.Serialization;
using Assembly = System.Reflection.Assembly;

namespace Bogus.DataSet.Vehicles;

public static class VehicleDataset {
    const string Filename = "vehicle_dataset.json";

    static readonly JsonSerializerOptions SerializerOptions =  new JsonSerializerOptions(JsonSerializerOptions.Default) {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters     = { new JsonStringEnumConverter() }
    };

    static VehicleDataset() {
        try {
            Data = Load();
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }

        return;

        static List<Vehicle> Load() {
            var assembly     = Assembly.GetAssembly(typeof(VehicleDataset))!;
            var resourceName = assembly.GetManifestResourceNames().First(name => name.EndsWith(Filename));

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            return JsonSerializer.Deserialize<List<Vehicle>>(stream, SerializerOptions)!;
        }
    }

    public static readonly List<Vehicle> Data;
}

public enum EngineType {
    Undefined = 0,
    Petrol    = 1,
    Diesel    = 2,
    Hybrid    = 3,
    Electric  = 4,
}

public record Vehicle {
    public string     Make       { get; set; } // Manufacturer: Ford, Toyota, Honda, etc.
    public string     Model      { get; set; } // Model: F150, Camry, Civic, etc.
    public int        Year       { get; set; } // Year of manufacture
    public EngineType EngineType { get; set; } // Engine type: Petrol, Diesel, Hybrid, Electric
}