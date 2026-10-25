namespace UbuntuLedger.Services;

// Rules for the PINs people choose. New PINs are six digits; older four-digit PINs still sign in.
public static class PinPolicy
{
    public const int Length = 6;

    // Null when the PIN is acceptable, otherwise what to tell the person.
    public static string? Check(string? pin)
    {
        if (pin is null || pin.Length != Length || !pin.All(char.IsAsciiDigit))
            return $"Your PIN must be exactly {Length} digits.";
        if (pin.Distinct().Count() == 1)
            return "Choose a PIN that is not one digit repeated.";
        if (IsRun(pin, 1) || IsRun(pin, -1))
            return "Choose a PIN that is not a simple run like 123456.";
        return null;
    }

    private static bool IsRun(string pin, int step)
    {
        for (var i = 1; i < pin.Length; i++)
            if (pin[i] - pin[i - 1] != step) return false;
        return true;
    }
}
