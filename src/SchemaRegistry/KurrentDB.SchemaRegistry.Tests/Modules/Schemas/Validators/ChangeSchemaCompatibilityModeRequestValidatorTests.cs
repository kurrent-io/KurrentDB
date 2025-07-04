// using Bogus;
// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.ChangeSchemaCompatibilityModeRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Validators;
//
// public class ChangeSchemaCompatibilityModeRequestValidatorTests {
//     Faker Faker { get; } = new();
//
//     [Test]
//     public void validate_with_accepted_values_should_be_valid() {
//         var request = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = Faker.Random.AlphaNumeric(10),
//             Compatibility = Faker.Random.Enum(CompatibilityMode.Undefined)
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test, InvalidSchemaVersionNameTestCases]
//     public void validate_with_empty_name_should_not_be_valid(string name) {
//         var request = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = name,
//             Compatibility = Faker.Random.Enum(CompatibilityMode.Undefined)
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(ChangeSchemaCompatibilityModeRequest.SchemaName));
//     }
//
//     [Test]
//     public void validate_with_invalid_compatibility_mode_should_not_be_valid() {
//         var request = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = Faker.Random.AlphaNumeric(10),
//             Compatibility = CompatibilityMode.Undefined
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(ChangeSchemaCompatibilityModeRequest.Compatibility));
//     }
//
//     public class InvalidSchemaVersionNameTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "";
//             yield return "   ";
//         }
//     }
// }