using YeaKazi.BuildingBlocks.Validation;

namespace YeaKazi.Organization.Domain;

public static class KenyanIdentifiers
{
    /// <summary>Employer KRA PIN (companies P..., individuals A...). See docs/assumptions.md A-06.</summary>
    public static bool IsValidEmployerKraPin(string? pin) => KenyanFormats.IsEmployerKraPin(pin);

    /// <summary>Individual KRA PIN per Section 15.</summary>
    public static bool IsValidIndividualKraPin(string? pin) => KenyanFormats.IsIndividualKraPin(pin);
}
