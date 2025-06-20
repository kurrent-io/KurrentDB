// using Bogus;
// using Google.Protobuf;
// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.ValidateSchemaRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Validators;
//
// public class ValidateSchemaRequestValidatorTests {
//     Faker Faker { get; } = new();
//
//     [Test]
//     public void validate_with_name_identifier_only_should_be_valid() {
//         var request = new ValidateSchemaRequest {
//             SchemaName = Faker.Random.AlphaNumeric(10),
//             Definition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test]
//     public void validate_with_name_and_version_identifiers_should_be_valid() {
//         var request = new ValidateSchemaRequest {
//
//             SchemaName    = Faker.Random.AlphaNumeric(10),
//             SchemaVersionId = Faker.Random.Number(min: 1, max: 10),
//             Definition    = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test]
//     public void validate_with_id_only_should_be_valid() {
//         var request = new ValidateSchemaRequest {
//             SchemaVersionId = Guid.NewGuid().ToString(),
//             Definition      = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test]
//     public void validate_with_missing_identifier_should_not_be_valid() {
//         var request = new ValidateSchemaRequest {
//             Definition = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//     }
//
//     [Test]
//     public void validate_with_id_and_name_should_not_be_valid() {
//         var instance = new ValidateSchemaRequest {
//             SchemaVersionId = Guid.NewGuid().ToString(),
//             SchemaName      = Faker.Random.AlphaNumeric(10),
//             VersionNumber   = Faker.Random.Number(min: 1, max: 10),
//             Definition      = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(instance);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(ValidateSchemaRequest.SchemaName));
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(ValidateSchemaRequest.VersionNumber));
//     }
//
//     [Test]
//     public void validate_with_invalid_version_should_not_be_valid() {
//         var request = new ValidateSchemaRequest {
//             SchemaName    = Faker.Random.AlphaNumeric(10),
//             VersionNumber = 0,
//             Definition    = ByteString.CopyFromUtf8(Faker.Lorem.Text())
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(x => x.PropertyName == nameof(ValidateSchemaRequest.VersionNumber));
//     }
//
//     [Test]
//     public void validate_with_missing_definition_should_be_not_be_valid() {
//         var request = new ValidateSchemaRequest {
//             SchemaName = Faker.Random.AlphaNumeric(10),
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(ValidateSchemaRequest.Definition));
//     }
// }