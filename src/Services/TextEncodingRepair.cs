using System.Text;

namespace RightMgr.Services;

public static class TextEncodingRepair
{
    private static readonly string[] MojibakeMarkers =
    [
        "浣跨", "敤璁", "颁簨", "鏈", "墦寮", "锛", "銆", "鈥", "鍥炴", "绔欐", "璇曪", "€", "�"
    ];

    static TextEncodingRepair()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string? RepairMojibake(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || SuspicionScore(value) == 0)
            return value;

        try
        {
            var legacyEncoding = Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var repaired = utf8.GetString(legacyEncoding.GetBytes(value));
            return SuspicionScore(repaired) < SuspicionScore(value) ? repaired : value;
        }
        catch (EncoderFallbackException)
        {
            return value;
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    private static int SuspicionScore(string value)
    {
        var score = MojibakeMarkers.Count(marker => value.Contains(marker, StringComparison.Ordinal));
        score += value.Count(ch => char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.PrivateUse);
        return score;
    }
}
