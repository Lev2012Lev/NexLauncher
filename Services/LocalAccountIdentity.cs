using System;
using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Auth;
using NexLauncher.Models;

namespace NexLauncher.Services;

/// <summary>Vanilla offline identity: Java UUID.nameUUIDFromBytes("OfflinePlayer:" + exactCaseName).</summary>
public static class LocalAccountIdentity
{
    public static bool IsValidUsername(string? username)
    {
        if (username is null || username.Length is < 3 or > 16) return false;
        foreach (var character in username)
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_')
                return false;
        return true;
    }

    public static string GetUuid(string username)
    {
        Validate(username);
        // MD5 is required by Minecraft's offline UUID scheme; this is identity, not cryptography.
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        // Do not use Guid(byte[]): .NET's mixed-endian layout differs from Java UUID byte order.
        return Convert.ToHexStringLower(bytes);
    }

    public static LauncherAccount CreateProfile(string username)
    {
        var uuid = GetUuid(username);
        return new LauncherAccount("local:" + uuid, username, uuid, Type: AccountType.Local);
    }

    public static MSession CreateSession(string username)
    {
        var uuid = GetUuid(username);
        var session = MSession.CreateOfflineSession(username);
        session.UUID = uuid;
        // CmlLib's factory defaults to msa; keep an explicit local discriminator for launch validation.
        session.UserType = "legacy";
        return session;
    }

    private static void Validate(string username)
    {
        if (!IsValidUsername(username))
            throw new ArgumentException("Ник должен содержать от 3 до 16 символов: латинские буквы, цифры или подчёркивание (_). Пробелы недопустимы.", nameof(username));
    }
}
