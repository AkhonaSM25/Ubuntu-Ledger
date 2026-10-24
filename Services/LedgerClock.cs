namespace UbuntuLedger.Services;

// "Today" for due dates and statuses. Ledger:Today in configuration pins it, so the demo data always looks the same.
public class LedgerClock(IConfiguration config)
{
    private readonly DateOnly? _fixed = DateOnly.TryParse(config["Ledger:Today"], out var d) ? d : null;

    public DateOnly Today => _fixed ?? DateOnly.FromDateTime(DateTime.Now);
}
