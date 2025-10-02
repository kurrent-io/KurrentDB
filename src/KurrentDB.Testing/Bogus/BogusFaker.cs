using Bogus;

namespace KurrentDB.Testing.Bogus;

/// <summary>
/// A parameterless version of Bogus.Faker for use with TUnit's ClassDataSource.
/// This allows TUnit to instantiate and inject a Faker instance into test classes.
/// </summary>
public class BogusFaker : Faker;
