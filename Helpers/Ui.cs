using System.Net;
using Microsoft.AspNetCore.Html;
using UbuntuLedger.Models;

namespace UbuntuLedger.Helpers;

// Small markup pieces shared by every view: icons, status chips, avatars and the geometric pattern.
public static class Ui
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["home"] = "m3 10 9-7 9 7M5 9v11h14V9M9 20v-6h6v6",
        ["users"] = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75",
        ["wallet"] = "M20 7V5a2 2 0 0 0-2-2H5a3 3 0 0 0 0 6h15v12H5a3 3 0 0 1-3-3V6M16 13h2",
        ["chart"] = "M4 19V9M10 19V5M16 19v-7M22 19H2",
        ["down"] = "M12 3v15M6 12l6 6 6-6M5 21h14",
        ["check"] = "M5 12l4 4L19 6",
        ["clock"] = "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM12 7v5l3 2",
        ["alert"] = "M12 9v4M12 17h.01M10.3 3.7 2.4 18a2 2 0 0 0 1.8 3h15.6a2 2 0 0 0 1.8-3L13.7 3.7a2 2 0 0 0-3.4 0Z",
        ["cal"] = "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2ZM16 3v4M8 3v4M3 10h18",
        ["coin"] = "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM9 9.5c0-1 1.3-1.8 3-1.8s3 .8 3 1.8-1.3 1.7-3 2.3-3 1.2-3 2.3 1.3 1.8 3 1.8 3-.8 3-1.8",
        ["mail"] = "M4 6h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1ZM3.5 7l8.5 6 8.5-6",
        ["shield"] = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10M9 12l2 2 4-4",
        ["user"] = "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z",
        ["plus"] = "M12 5v14M5 12h14",
        ["chevronRight"] = "m9 18 6-6-6-6",
        ["chevronLeft"] = "M15 18l-6-6 6-6",
        ["updown"] = "m7 15 5 5 5-5M7 9l5-5 5 5",
        ["signout"] = "M10 17l5-5-5-5M15 12H3M15 3h6v18h-6",
        ["close"] = "M6 6l12 12M18 6 6 18",
        ["search"] = "M18 11a7 7 0 1 1-14 0 7 7 0 0 1 14 0ZM20 20l-4-4",
        ["download"] = "M12 3v12M7 10l5 5 5-5M5 21h14",
        ["contacts"] = "M6 3h10a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2ZM13.5 10a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0ZM7 17c.6-2 2-3 4-3s3.4 1 4 3M21 8v3M21 14v3",
        ["lock"] = "M7 11h10a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2ZM8 11V8a4 4 0 0 1 8 0v3",
    };

    public static IHtmlContent Icon(string name, int size = 20, double stroke = 2) => new HtmlString(
        $"<svg aria-hidden=\"true\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
        $"stroke-width=\"{stroke.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" style=\"flex:0 0 auto\"><path d=\"{Icons[name]}\"/></svg>");

    // Colour never works alone: every chip carries an icon and a word.
    public static IHtmlContent Chip(string kind, string? label = null)
    {
        var (defaultLabel, icon) = kind switch
        {
            "paid" => ("Paid", "check"),
            "ok" => ("Up to date", "check"),
            "soon" => ("Due soon", "clock"),
            "overdue" => ("Overdue", "alert"),
            "invited" => ("Invited", "mail"),
            "treasurer" => ("Treasurer", "shield"),
            "awaiting" => ("Awaiting you", "clock"),
            "waiting" => ("Waiting", "clock"),
            "disputed" => ("Disputed", "alert"),
            "voided" => ("Voided", "close"),
            _ => ("Member", "user"),
        };
        return new HtmlString($"<span class=\"chip chip--{kind}\">{Html(Icon(icon, 14, 2.4))}{WebUtility.HtmlEncode(label ?? defaultLabel)}</span>");
    }

    public static IHtmlContent Chip(LoanStatus status) => Chip(StatusKey(status));

    public static string StatusKey(LoanStatus s) => s switch
    {
        LoanStatus.Paid => "paid",
        LoanStatus.Overdue => "overdue",
        LoanStatus.DueSoon => "soon",
        LoanStatus.Voided => "voided",
        _ => "ok",
    };

    // size: "44", "40", "big" or "square"
    public static IHtmlContent Avatar(string text, string size = "44", bool solid = false) => new HtmlString(
        $"<span class=\"avatar {(size == "square" ? "avatar--square" : "avatar--" + size)}{(solid ? " avatar--solid" : "")}\" aria-hidden=\"true\">{WebUtility.HtmlEncode(text)}</span>");

    public static IHtmlContent Geo(bool dark = false, bool right = false, double? opacity = null) => new HtmlString(
        $"<svg aria-hidden=\"true\" class=\"geo{(right ? " geo--right" : "")}\"{(opacity is { } o ? $" style=\"opacity:{o.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"" : "")}>" +
        $"<rect width=\"100%\" height=\"100%\" fill=\"url(#ul-geo-{(dark ? "dark" : "light")})\"/></svg>");

    public static IHtmlContent LogoMark(int size = 26) => new HtmlString(
        $"<svg aria-hidden=\"true\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 32 32\" fill=\"none\"><circle cx=\"16\" cy=\"16\" r=\"11\" stroke=\"#fff\" stroke-width=\"3\"/>" +
        "<circle cx=\"16\" cy=\"5\" r=\"3\" fill=\"#E0A800\"/><circle cx=\"6.5\" cy=\"21.5\" r=\"3\" fill=\"#fff\"/><circle cx=\"25.5\" cy=\"21.5\" r=\"3\" fill=\"#fff\"/></svg>");

    private static string Html(IHtmlContent c)
    {
        using var w = new StringWriter();
        c.WriteTo(w, System.Text.Encodings.Web.HtmlEncoder.Default);
        return w.ToString();
    }
}
