namespace UbuntuLedger.Models;

public static class PersonExtensions
{
    public static string InitialsOf(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(2));
}

public static class PhoneNumber
{
    // "082 555 0148" and "0825550148" are the same number.
    public static string Normalize(string? phone) => new((phone ?? "").Where(char.IsDigit).ToArray());

    // 0825550148 -> 27825550148, the form WhatsApp links need. Numbers already in international form pass through.
    public static string ToInternational(string? phone)
    {
        var digits = Normalize(phone);
        if (digits.StartsWith("00")) return digits[2..];
        if (digits.StartsWith('0')) return "27" + digits[1..];
        return digits;
    }

    // 0825550148 -> 082 555 0148
    public static string Format(string digits) =>
        digits.Length == 10 ? $"{digits[..3]} {digits[3..6]} {digits[6..]}" : digits;
}
