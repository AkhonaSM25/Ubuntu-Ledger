using Microsoft.EntityFrameworkCore;
using UbuntuLedger.Models;
using UbuntuLedger.Services;

namespace UbuntuLedger.Tests;

public class AccountTests
{
    [Theory]
    [InlineData("482915", true)]
    [InlineData("135790", true)]
    [InlineData("1234", false)]       // too short
    [InlineData("1234567", false)]    // too long
    [InlineData("12ab56", false)]     // not digits
    [InlineData("111111", false)]     // one digit repeated
    [InlineData("123456", false)]     // ascending run
    [InlineData("654321", false)]     // descending run
    [InlineData(null, false)]
    public void Pin_policy_accepts_only_six_unguessable_digits(string? pin, bool ok) =>
        Assert.Equal(ok, PinPolicy.Check(pin) is null);

    [Fact]
    public async Task Correct_pin_signs_in_and_wrong_pin_does_not_say_which_part_was_wrong()
    {
        using var t = await TestDb.SeededAsync();
        Assert.NotNull((await t.Accounts.SignInAsync(SeedData_Phone, Data.SeedData.DemoPin)).Person);

        var wrongPin = await t.Accounts.SignInAsync(SeedData_Phone, "999999");
        var wrongPhone = await t.Accounts.SignInAsync("0800000000", "999999");
        Assert.Null(wrongPin.Person);
        Assert.Equal(wrongPin.Error, wrongPhone.Error);
    }

    private const string SeedData_Phone = Data.SeedData.TreasurerPhone;

    [Fact]
    public async Task Five_wrong_pins_lock_the_account_even_for_the_right_pin_until_the_lock_runs_out()
    {
        using var t = await TestDb.SeededAsync();
        for (var i = 0; i < AccountService.MaxFailedAttempts; i++)
            await t.Accounts.SignInAsync(SeedData_Phone, "999999");

        var locked = await t.Accounts.SignInAsync(SeedData_Phone, Data.SeedData.DemoPin);
        Assert.Null(locked.Person);
        Assert.Contains("Too many wrong PINs", locked.Error);

        t.Time.Advance(AccountService.LockFor + TimeSpan.FromMinutes(1));
        Assert.NotNull((await t.Accounts.SignInAsync(SeedData_Phone, Data.SeedData.DemoPin)).Person);
    }

    [Fact]
    public async Task A_good_sign_in_resets_the_wrong_pin_count()
    {
        using var t = await TestDb.SeededAsync();
        for (var i = 0; i < AccountService.MaxFailedAttempts - 1; i++)
            await t.Accounts.SignInAsync(SeedData_Phone, "999999");
        Assert.NotNull((await t.Accounts.SignInAsync(SeedData_Phone, Data.SeedData.DemoPin)).Person);
        for (var i = 0; i < AccountService.MaxFailedAttempts - 1; i++)
            await t.Accounts.SignInAsync(SeedData_Phone, "999999");
        Assert.NotNull((await t.Accounts.SignInAsync(SeedData_Phone, Data.SeedData.DemoPin)).Person);
    }

    [Fact]
    public async Task An_invite_link_creates_the_account_joins_the_circle_and_works_once()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        Assert.True((await t.Circles.InviteAsync(zanele, "Lindiwe Zulu", "082 771 5530")).Succeeded);
        var invite = await t.Db.Invitations.SingleAsync(i => i.Phone == "0827715530");
        Assert.False(string.IsNullOrEmpty(invite.Token));

        Assert.Contains("agree", (await t.Accounts.JoinAsync(invite.Token, "Lindiwe Zulu", "482915", "482915", consent: false)).Error);
        Assert.NotNull((await t.Accounts.JoinAsync(invite.Token, "Lindiwe Zulu", "123456", "123456", true)).Error);   // weak PIN
        Assert.NotNull((await t.Accounts.JoinAsync(invite.Token, "Lindiwe Zulu", "482915", "482916", true)).Error);   // mismatch

        var joined = await t.Accounts.JoinAsync(invite.Token, "Lindiwe Zulu", "482915", "482915", true);
        Assert.Null(joined.Error);
        Assert.NotNull(joined.Person!.ConsentedAt);
        Assert.NotNull((await t.Accounts.SignInAsync("0827715530", "482915")).Person);
        Assert.Equal(CircleRole.Member, (await t.Access.GetAsync(joined.CircleId, joined.Person.Id))!.Role);

        Assert.Null(await t.Accounts.FindJoinInviteAsync(invite.Token));   // used up
        Assert.NotNull((await t.Accounts.JoinAsync(invite.Token, "Someone Else", "482915", "482915", true)).Error);
    }

    [Fact]
    public async Task Unknown_or_oversized_join_tokens_find_nothing()
    {
        using var t = await TestDb.SeededAsync();
        Assert.Null(await t.Accounts.FindJoinInviteAsync("nope"));
        Assert.Null(await t.Accounts.FindJoinInviteAsync(null));
        Assert.Null(await t.Accounts.FindJoinInviteAsync(new string('a', 200)));
    }

    [Fact]
    public async Task Treasurer_reset_link_sets_a_new_pin_once_and_only_its_hash_is_stored()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.PersonAsync("Nomsa Khumalo");

        var (token, error) = await t.Accounts.CreatePinResetAsync(zanele, thandi.Id);
        Assert.Null(error);
        var stored = await t.Db.People.AsNoTracking().SingleAsync(p => p.Id == thandi.Id);
        Assert.NotEqual(token, stored.PinResetTokenHash);
        Assert.Equal(Tokens.Hash(token!), stored.PinResetTokenHash);

        Assert.NotNull((await t.Accounts.ResetPinAsync(token, "123456", "123456")).Error);   // weak
        Assert.True((await t.Accounts.ResetPinAsync(token, "482915", "482915")).Succeeded);
        Assert.NotNull((await t.Accounts.SignInAsync(thandi.Phone, "482915")).Person);
        Assert.False((await t.Accounts.ResetPinAsync(token, "593016", "593016")).Succeeded);  // single use
    }

    [Fact]
    public async Task Reset_links_expire_and_members_cannot_create_them()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var sipho = await t.AccessAsync("Sipho Dlamini");
        var thandi = await t.PersonAsync("Nomsa Khumalo");

        Assert.NotNull((await t.Accounts.CreatePinResetAsync(sipho, thandi.Id)).Error);
        Assert.NotNull((await t.Accounts.CreatePinResetAsync(zanele, zanele.Me.Id)).Error);   // not your own

        var (token, _) = await t.Accounts.CreatePinResetAsync(zanele, thandi.Id);
        t.Time.Advance(AccountService.ResetLinkLife + TimeSpan.FromMinutes(1));
        Assert.Null(await t.Accounts.FindResetAsync(token));
    }

    [Fact]
    public async Task A_treasurer_cannot_reset_someone_who_is_also_in_another_circle()
    {
        using var t = await TestDb.SeededAsync();
        var zanele = await t.AccessAsync("Zanele Maseko");
        var thandi = await t.PersonAsync("Thandi Mokoena");   // also treasurer of Mokoena Savers

        var result = await t.Accounts.CreatePinResetAsync(zanele, thandi.Id);
        Assert.Null(result.Token);
        Assert.Contains("another circle", result.Error);
    }
}
