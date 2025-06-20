// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.SchemaRegistry.GetSchemaVersionRequestValidator;
//
// namespace KurrentDB.SchemaRegistry.Tests.Queries.Validators;
//
// public class GetSchemaVersionRequestValidatorTests {
//     [Test, InvalidSchemaVersionIdTestCases]
//     public void validate_with_empty_id_should_not_be_valid(string id) {
//         var request = new GetSchemaVersionRequest {
//             SchemaVersionId = id
//         };
//
//         var result = Instance.Validate(request);
//
//         result.IsValid.Should().BeFalse();
//         result.Errors.Should().Contain(v => v.PropertyName == nameof(GetSchemaVersionRequest.SchemaVersionId));
//     }
//
//     public class InvalidSchemaVersionIdTestCases : TestCaseGenerator<string> {
//         protected override IEnumerable<string> Data() {
//             yield return "";
//             yield return "   ";
//         }
//     }
// }