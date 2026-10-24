using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.ViewModels;

public record StatCard(string Label, string Value, string Note, string Icon, bool Featured = false);

// One borrower in a circle with their totals and worst status across open loans.
public record MemberSummary(Person Person, IReadOnlyList<LoanSummary> Loans, IReadOnlyList<LoanSummary> Active, decimal Owed, LoanStatus Status)
{
    public static MemberSummary From(Person p, IEnumerable<LoanSummary> all)
    {
        var loans = all.Where(l => l.Loan.BorrowerId == p.Id).ToList();
        var active = loans.Where(l => l.IsOpen).ToList();
        var status = active.Any(l => l.Status == LoanStatus.Overdue) ? LoanStatus.Overdue
            : active.Any(l => l.Status == LoanStatus.DueSoon) ? LoanStatus.DueSoon
            : LoanStatus.UpToDate;
        return new MemberSummary(p, loans, active, active.Sum(l => l.Balance), status);
    }
}

// A summary line in a confirmation dialog.
public record ConfirmLine(string Label, string Value, bool Strong = false);
