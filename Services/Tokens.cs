using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace UbuntuLedger.Services;

// Random secrets for join links and PIN reset links. 128 bits from the system's secure random generator.
public static class Tokens
{
    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    // Reset tokens are stored hashed, so a copy of the database cannot be used to take over an account.
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
