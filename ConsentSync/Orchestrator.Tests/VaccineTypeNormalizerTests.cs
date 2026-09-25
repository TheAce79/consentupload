using ConsentSyncCore.Services;
using Xunit;

namespace Orchestrator.Tests;

public sealed class VaccineTypeNormalizerTests
{
    [Theory]
    [InlineData("PS", "Preschool Appointment")]
    [InlineData("Rendez-vous préscolaire", "Preschool Appointment")]
    [InlineData("2 mois", "2 Month Appointment")]
    [InlineData("2m", "2 Month Appointment")]
    [InlineData("Rendez-vous 4 mois", "4 Month Appointment")]
    [InlineData("6 Month Appointment", "6 Month Appointment")]
    [InlineData("Vaccination 12 mois", "12 Month Appointment")]
    [InlineData("18m", "18 Month Appointment")]
    [InlineData("Autre", "Other / Autre")]
    [InlineData("Rattrapage", "Other / Autre")]
    [InlineData("Unexpected French label", "Other / Autre")]
    [InlineData("Unknown", "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("   ", "Unknown")]
    public void Normalize_MapsSupportedAndFallbackLabels(string? rawVaccineType, string expected)
        => Assert.Equal(expected, VaccineTypeNormalizer.Normalize(rawVaccineType));

    [Fact]
    public void Normalize_IsStableForNormalizedValues()
    {
        const string normalized = "4 Month Appointment";
        Assert.Equal(normalized, VaccineTypeNormalizer.Normalize(VaccineTypeNormalizer.Normalize(normalized)));
    }
}
