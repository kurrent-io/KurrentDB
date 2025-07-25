// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// using Bogus;
// using Google.Protobuf;
// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.RegisterSchemaRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Validators;
//
// public class RegisterSchemaRequestValidatorTests {
//     Faker Faker { get; } = new();
//
//     [Test]
//     public void validate_with_accepted_values_should_be_valid() {
//         var request = RandomRequest();
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test, EmptySchemaNameTestCases]
//     public void validate_with_empty_or_null_name_should_not_be_valid(string name) {
//         var request = RandomRequest(name: name);
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(RegisterSchemaRequest.SchemaName));
//     }
//
//     [Test, InvalidSchemaNameTestCases]
//     public void validate_with_invalid_characters_in_name_should_not_be_valid(string name) {
//         var request = RandomRequest(name: name);
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(RegisterSchemaRequest.SchemaName));
//     }
//
//     [Test]
//     public void validate_with_invalid_schema_format_should_not_be_valid() {
//         var request = RandomRequest(format: SchemaFormat.Undefined);
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(RegisterSchemaRequest.Format));
//     }
//
//     [Test]
//     public void validate_with_empty_definitions_should_not_be_valid() {
//         var request = RandomRequest(definition: ByteString.Empty);
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(RegisterSchemaRequest.Definition));
//     }
//
//     [Test]
//     public void validate_with_invalid_compatibility_level_should_not_be_valid() {
//         var request = RandomRequest(compatibility: CompatibilityMode.Undefined);
//         var result  = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(RegisterSchemaRequest.Compatibility));
//     }
//
//     private RegisterSchemaRequest RandomRequest(string? name = null, SchemaFormat? format = null, ByteString? definition = null, CompatibilityMode? compatibility = null) =>
//         new RegisterSchemaRequest {
//             SchemaName    = name          ?? Faker.Random.AlphaNumeric(10),
//             Format        = format        ?? Faker.Random.Enum(SchemaFormat.Undefined),
//             Definition    = definition    ?? ByteString.CopyFromUtf8(Faker.Lorem.Text()),
//             Compatibility = compatibility ?? Faker.Random.Enum(CompatibilityMode.Undefined)
//         };
//
//     public class EmptySchemaNameTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "";
//             yield return "   ";
//         }
//     }
//
//     public class InvalidSchemaNameTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "Invalid Subject";
//             yield return "Invalid@Subject";
//             yield return "Invalid!";
//         }
//     }
// }