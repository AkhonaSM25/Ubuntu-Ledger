# Personal information held (POPIA working notes)

This is a working inventory to support the privacy notice. It is not legal advice; have it reviewed before launch.

| Information | Why | Where | Kept |
|---|---|---|---|
| Name, phone number | Identify members and let them sign in | People table | While the account exists |
| PIN | Sign-in | Stored as a hash only | Until changed |
| Consent time, PIN change time, failed-attempt count, lock time | Prove consent; protect the account | People table | While the account exists |
| Loans, repayments, void reasons | The lending record itself | Loans, Repayments | While the circle exists |
| Payment proofs (images/PDF) | Evidence of a payment | Proofs folder | With the payment |
| Invitations, join and reset tokens | Joining and PIN reset | Invitations; resets stored as a hash | Until used or expired |
| Backups | Recovery | Backups folder / Blob container | Per retention setting |

**Who sees what.** Members see their own loans and payments. The treasurer sees all loans in their circle. A member sees a treasurer's own loan payments only when asked to confirm them.
**Corrections** are recorded, never deleted, so a reversed entry stays visible with its reason.
**Open questions:** deletion and export requests (process and timing), retention after a circle ends, an appointed Information Officer, and the contact address in `Legal:ContactEmail`.
