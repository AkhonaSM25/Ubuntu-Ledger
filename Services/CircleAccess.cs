using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Data;
using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

// The signed-in person's place in one circle. Treasurers see and change everything; members see only their own loans.
public record CircleAccess(Circle Circle, Person Me, CircleRole Role)
{
    public bool IsTreasurer => Role == CircleRole.Treasurer;

    public bool CanSee(Loan loan) => IsTreasurer || loan.BorrowerId == Me.Id;
}

public class CircleAccessService(LedgerDbContext db)
{
    public async Task<CircleAccess?> GetAsync(int circleId, int personId)
    {
        var m = await db.Memberships
            .Include(x => x.Circle)
            .Include(x => x.Person)
            .FirstOrDefaultAsync(x => x.CircleId == circleId && x.PersonId == personId);
        return m is null ? null : new CircleAccess(m.Circle, m.Person, m.Role);
    }
}
