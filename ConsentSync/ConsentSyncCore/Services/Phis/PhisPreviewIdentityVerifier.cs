using System.Net.Mail;
using System.Text.RegularExpressions;
using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Phis;

/// <summary>Pure comparison rules for accepting an already-selected PHIS preview.</summary>
public static partial class PhisPreviewIdentityVerifier
{
    public static string? GetMatchingIdentifier(string? medicare, string? phone, string? email, PhisClientPreview? preview, string expectedClientId)
    {
        if (preview is null || !string.Equals(preview.ClientId, expectedClientId, StringComparison.Ordinal)) return null;
        if (SameMedicare(medicare, preview.HealthCardNumber)) return "Medicare";
        if (SamePhone(phone, preview.PreferredTelephoneNumber)) return "PreferredTelephone";
        if (SameEmail(email, preview.EmailAddresses)) return "Email";
        return null;
    }

    internal static bool SameMedicare(string? left, string? right)
    {
        if (!IsMedicareFormat(left) || !IsMedicareFormat(right)) return false;
        string a = NormalizeMedicare(left); string b = NormalizeMedicare(right);
        return a.Length > 0 && a == b;
    }

    internal static bool SamePhone(string? left, string? right)
    {
        if (!IsPhoneFormat(left) || !IsPhoneFormat(right)) return false;
        string a = NormalizePhone(left); string b = NormalizePhone(right);
        return a.Length == 10 && a == b;
    }

    internal static bool SameEmail(string? email, IReadOnlyList<PhisEmailAddress> addresses) =>
        IsValidEmail(email) && addresses.Any(address => IsValidEmail(address.Address) &&
            string.Equals(email!.Trim(), address.Address.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static string NormalizeMedicare(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : string.Concat(value.Where(char.IsDigit));
    internal static string NormalizePhone(string? value)
    {
        string digits = string.IsNullOrWhiteSpace(value) ? string.Empty : string.Concat(value.Where(char.IsDigit));
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }
    private static bool IsMedicareFormat(string? value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-');
    private static bool IsPhoneFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        bool plusSeen = false;
        foreach (char c in value)
        {
            if (char.IsDigit(c) || char.IsWhiteSpace(c) || c is '(' or ')' or '-' or '.') continue;
            if (c == '+' && !plusSeen) { plusSeen = true; continue; }
            return false;
        }
        return value.TrimStart().IndexOf('+') is -1 or 0;
    }
    internal static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new MailAddress(value).Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
