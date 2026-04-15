using System.Security.Cryptography;
using System.Text;

namespace AIM.Web.Services;

public static class LinkAnalysis
{
    public static string? BuildLinkId(string? einSsn, string? dob)
    {
        if (string.IsNullOrWhiteSpace(einSsn) && string.IsNullOrWhiteSpace(dob)) return null;
        var value = $"{einSsn}|{dob}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..6].ToLowerInvariant();
    }
}
