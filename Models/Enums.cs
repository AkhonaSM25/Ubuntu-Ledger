namespace UbuntuLedger.Models;

public enum CircleRole
{
    Member = 0,
    Treasurer = 1
}

public enum InterestType
{
    // One flat charge on the principal, added when the loan starts.
    Simple = 0,
    // The rate is charged on the principal every month of the term.
    Monthly = 1
}

public enum PaymentMethod
{
    EFT = 0,
    Cash = 1
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3
}

public enum LoanStatus
{
    Paid = 0,
    UpToDate = 1,
    DueSoon = 2,
    Overdue = 3
}
