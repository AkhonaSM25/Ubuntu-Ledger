using UbuntuLedger.Models;

namespace UbuntuLedger.Services;

// Links that open WhatsApp with a message ready to send. Nothing is sent from the server: the person taps Send.
public static class WhatsApp
{
    // With a number the chat opens straight away; without one, WhatsApp lets the person pick who to send it to.
    public static string Link(string? phone, string message)
    {
        var number = PhoneNumber.ToInternational(phone);
        return $"https://wa.me/{number}?text={Uri.EscapeDataString(message)}";
    }
}
