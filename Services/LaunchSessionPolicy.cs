using System;
using CmlLib.Core.Auth;

namespace NexLauncher.Services;

/// <summary>Copies only complete Microsoft sessions or explicitly constructed local sessions for launch.</summary>
public static class LaunchSessionPolicy
{
    public static MSession CopyForLaunch(MSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.UserType == "legacy")
        {
            // Local identities never carry Xbox credentials. Rebuild them from the validated name
            // instead of retaining arbitrary values supplied by a caller or a mutable session.
            if (!LocalAccountIdentity.IsValidUsername(session.Username)
                || session.AccessToken != "access_token"
                || !string.IsNullOrEmpty(session.ClientToken)
                || !string.IsNullOrEmpty(session.Xuid)
                || !string.Equals(session.UUID, LocalAccountIdentity.GetUuid(session.Username!), StringComparison.Ordinal))
                throw new InvalidOperationException("Некорректный локальный аккаунт. Создайте его заново в настройках аккаунтов.");

            return LocalAccountIdentity.CreateSession(session.Username!);
        }

        // A failed or expired Microsoft sign-in must never become a local launch implicitly.
        // CmlLib's unmodified offline factory also uses "msa" plus a placeholder token: reject it.
        if (!session.CheckIsValid() || session.UserType != "msa"
            || !Guid.TryParse(session.UUID, out _)
            || session.AccessToken is "access_token" or "0")
            throw new InvalidOperationException("Выберите локальный аккаунт или войдите через Microsoft с Minecraft Java Edition.");

        return new MSession(session.Username, session.AccessToken, session.UUID)
        {
            UserType = session.UserType,
            ClientToken = session.ClientToken,
            Xuid = session.Xuid
        };
    }
}
