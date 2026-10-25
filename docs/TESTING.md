# Testing and quality

## Automated (runs on every push and pull request)
`.github/workflows/ci.yml` restores, builds in Release, runs all tests with coverage, and fails if any package, including indirect ones, has a known vulnerability. Coverage appears in the run summary. Dependabot (`.github/dependabot.yml`) opens pull requests for newer or safer packages each week.

Run the same locally:
```bash
dotnet test UbuntuLedger.slnx
dotnet list package --vulnerable --include-transitive
```

## What the tests cover
| Area | Examples |
|---|---|
| Loan maths | flat and monthly interest, instalments, overdue and due-soon, voided loans |
| Payments | two-party confirmation, disputes, cancels, duplicate guard, balance and date checks |
| Corrections | reversing a payment, voiding a loan, totals ignoring voided loans |
| Accounts | PIN rules, lockout and unlock, join links, PIN reset, changing your PIN |
| Access | members cannot see other people's loans or the treasurer's pages; outsiders get 404 |
| Proofs | real files only, size and type limits, no path tricks, only people who can see the loan |
| Web | sign-in redirect, public legal pages, `/health`, security headers, missing anti-forgery token refused |
| Exports | spreadsheet formula text neutralised |

## Manual checks (record the result here when done)
Automated tests cannot judge how the app looks or feels. Do these on the live site before real users.

| Check | How | Result / date |
|---|---|---|
| Accessibility scan | Install the axe DevTools browser extension. Run it on Login, Join, Loans, Loan details, Payments, Payment details, Members and Privacy. Fix anything Serious or Critical | |
| Lighthouse | Chrome DevTools > Lighthouse, mobile, for the same pages. Note Performance, Accessibility, Best Practices | |
| Keyboard only | Sign in, record a loan, confirm a payment with Tab, Enter, Space and Esc only | |
| Real phone | Open the live link on an Android phone and an iPhone. Check forms, the confirm dialog and the WhatsApp buttons | |
| Slow network | DevTools > Network > Slow 4G. Tap submit buttons twice; only one payment should be made | |
| Backup restore | Take a backup, restore it on a copy, sign in and check balances match | |
| Pilot | Run the app with the real circle (5 to 10 people) for a week and note feedback | |

## Known gaps
- Browser-level (end-to-end) tests and automated accessibility scans are not in CI.
- Coverage is reported, not enforced.
