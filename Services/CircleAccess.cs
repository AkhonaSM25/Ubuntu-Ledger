using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

// The signed-in person's place in one circle. Treasurers see and change everything; members see only their own loans.
public record CircleAccess(Circle Circle, Person Me, CircleRole Role, int TreasurerId)
{
    public bool IsTreasurer => Role == CircleRole.Treasurer;

    public bool CanSee(Loan loan) => IsTreasurer || loan.BorrowerId == Me.Id;

    // The treasurer is a member too and can borrow. Nobody can confirm their own payment, so on the treasurer's
    // loans any other member confirms it. That member sees the payment, but never the loan or its balance.
    public bool IsWitnessFor(int borrowerId) => borrowerId == TreasurerId && Me.Id != borrowerId;

    public bool CanSeePayment(Loan loan) => CanSee(loan) || IsWitnessFor(loan.BorrowerId);
}

public class CircleAccessService(LedgerDbContext db)
{
    public async Task<CircleAccess?> GetAsync(int circleId, int personId)
    {
        var m = await db.Memberships
            .Include(x => x.Circle)
            .Include(x => x.Person)
            .FirstOrDefaultAsync(x => x.CircleId == circleId && x.PersonId == personId);
        if (m is null) return null;
        var treasurerId = await db.Memberships
            .Where(x => x.CircleId == circleId && x.Role == CircleRole.Treasurer)
            .Select(x => x.PersonId).FirstOrDefaultAsync();
        return new CircleAccess(m.Circle, m.Person, m.Role, treasurerId);
    }
}
