// using Bogus;
// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.GetSchemaRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Queries.Validators;
//
// public class GetSchemaRequestValidatorTests {
//     [Test]
//     public void validate_with_valid_name_should_be_valid() {
//         var request = new GetSchemaRequest {
//             SchemaName = nameof(Person)
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeTrue();
//         result.Errors.Should().BeEmpty();
//     }
//
//     [Test, InvalidSchemaNameTestCases]
//     public void validate_with_empty_name_should_not_be_valid(string name) {
//         var request = new GetSchemaRequest {
//             SchemaName = name
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(GetSchemaRequest.SchemaName));
//     }
//
//     public class InvalidSchemaNameTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "";
//             yield return "   ";
//         }
//     }
// }