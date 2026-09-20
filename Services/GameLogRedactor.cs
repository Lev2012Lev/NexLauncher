using System;
using System.Text.RegularExpressions;

namespace NexLauncher.Services;

internal static partial class GameLogRedactor
{
    internal static string Redact(string line, params string?[] secrets)
    {
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
                line = line.Replace(secret, "[скрыто]", StringComparison.Ordinal);
        }
        return TokenPattern().Replace(line, "$1[скрыто]");
    }

    [GeneratedRegex(@"((?:--accessToken|--clientToken|access_token|refresh_token|Authorization)\s*[=:]?\s*(?:Bearer\s+)?)[^\s,\]}]+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();
}
