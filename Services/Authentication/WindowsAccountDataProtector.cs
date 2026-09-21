using System;
using System.Security.Cryptography;
using System.Text;

namespace NexLauncher.Services.Authentication;

public sealed class WindowsAccountDataProtector : IAccountDataProtector
{
    // Domain separation, not a secret key. DPAPI owns the current Windows user's key.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexLauncher.AccountVault.v1");

    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Защищённое хранилище аккаунтов пока доступно только в Windows.");
        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Защищённое хранилище аккаунтов пока доступно только в Windows.");
        return ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
    }
}
