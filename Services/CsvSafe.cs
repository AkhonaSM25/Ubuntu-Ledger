namespace UbuntuLedger.Services;

// Spreadsheet programs run text that starts with = + - @ as a formula. Names and notes in an export come from users,
// so such text is made harmless with a leading apostrophe before it is quoted.
public static class CsvSafe
{
    public static string Text(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
