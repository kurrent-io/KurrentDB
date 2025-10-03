using Bogus;

namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Bogus extension to enable faker.HomeAutomation() syntax
/// </summary>
public static class FakerExtensions {
    public static HomeAutomationDataSet HomeAutomation(this Faker faker, string locale = "en") {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale, nameof(locale));
        return faker.Locale != locale ? new HomeAutomationDataSet(locale) : new HomeAutomationDataSet(faker);
    }
}
