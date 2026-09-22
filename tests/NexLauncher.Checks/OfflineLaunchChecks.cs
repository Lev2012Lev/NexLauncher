using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using NexLauncher.Services;

internal static class OfflineLaunchChecks
{
    public static void Run(Action<bool, string> check)
    {
        var local = LocalAccountIdentity.CreateSession("Local_Player");
        var launch = LaunchSessionPolicy.CopyForLaunch(local);
        check(launch.CheckIsValid() && launch.UserType == "legacy" && launch.AccessToken == "access_token"
              && launch.UUID == LocalAccountIdentity.GetUuid("Local_Player")
              && string.IsNullOrEmpty(launch.ClientToken) && string.IsNullOrEmpty(launch.Xuid),
            "explicit local account produces a complete CmlLib launch session without Microsoft credentials");
        var localArguments = BuildArguments(launch);
        check(localArguments.Contains("--username Local_Player", StringComparison.Ordinal)
              && localArguments.Contains("--uuid " + launch.UUID, StringComparison.Ordinal)
              && localArguments.Contains("--accessToken access_token", StringComparison.Ordinal)
              && localArguments.Contains("--userType legacy", StringComparison.Ordinal)
              && !localArguments.Contains("${", StringComparison.Ordinal),
            "installed CmlLib builds ordinary offline game arguments with exact username, UUID and legacy type");
        local.Username = "Changed";
        local.UUID = Guid.NewGuid().ToString("N");
        check(!ReferenceEquals(launch, local) && launch.Username == "Local_Player"
              && launch.UUID == LocalAccountIdentity.GetUuid("Local_Player"),
            "launch session is isolated from later account-session mutation");

        Rejects(() => LaunchSessionPolicy.CopyForLaunch(MSession.CreateOfflineSession("Guest")), check,
            "raw CmlLib msa placeholder cannot silently become an offline account");
        RejectLocal(session => session.UUID = Guid.NewGuid().ToString("N"), check,
            "local session rejects UUID unrelated to its exact player name");
        RejectLocal(session => session.Username = "bad name", check,
            "local session rejects invalid username before network or installation");
        RejectLocal(session => session.AccessToken = "test-microsoft-token", check,
            "local session never accepts a Microsoft access token");
        RejectLocal(session => session.AccessToken = "0", check,
            "local session uses CmlLib placeholder rather than zero that would damage numeric log text");
        RejectLocal(session => session.ClientToken = "test-client-token", check,
            "local session rejects client credentials");
        RejectLocal(session => session.Xuid = "123456", check,
            "local session rejects Xbox credentials");

        var microsoft = new MSession("MicrosoftUser", "test-microsoft-token", "123456781234423482341234567890ab")
        {
            UserType = "msa", ClientToken = "test-client-token", Xuid = "123456"
        };
        var microsoftLaunch = LaunchSessionPolicy.CopyForLaunch(microsoft);
        check(!ReferenceEquals(microsoft, microsoftLaunch) && microsoftLaunch.UserType == "msa"
              && microsoftLaunch.Username == microsoft.Username && microsoftLaunch.UUID == microsoft.UUID
              && microsoftLaunch.AccessToken == microsoft.AccessToken
              && microsoftLaunch.ClientToken == microsoft.ClientToken && microsoftLaunch.Xuid == microsoft.Xuid,
            "Microsoft launch copies and preserves every existing session credential");
        var microsoftArguments = BuildArguments(microsoftLaunch);
        check(microsoftArguments.Contains("--userType msa", StringComparison.Ordinal)
              && microsoftArguments.Contains("--accessToken test-microsoft-token", StringComparison.Ordinal)
              && microsoftArguments.Contains("--uuid " + microsoft.UUID, StringComparison.Ordinal),
            "installed CmlLib retains Microsoft type, token and UUID in launch arguments");
        foreach (var token in new string?[] { null, "", "access_token", "0" })
        {
            microsoft.AccessToken = token;
            Rejects(() => LaunchSessionPolicy.CopyForLaunch(microsoft), check,
                "missing or placeholder Microsoft token cannot fall back to local mode");
        }
        microsoft.AccessToken = "test-microsoft-token";
        microsoft.UUID = "not-a-uuid";
        Rejects(() => LaunchSessionPolicy.CopyForLaunch(microsoft), check, "malformed Microsoft UUID is still rejected");
        microsoft.UUID = "123456781234423482341234567890ab";
        microsoft.UserType = "unknown";
        Rejects(() => LaunchSessionPolicy.CopyForLaunch(microsoft), check, "unknown session type is rejected");
    }

    private static string BuildArguments(MSession session)
    {
        // Exercise the real package's process builder without downloading files or starting Java.
        using var version = new JsonVersion(JsonDocument.Parse("""
            {"id":"test","type":"release","mainClass":"net.minecraft.client.main.Main",
             "libraries":[],"arguments":{"game":["--username","${auth_player_name}",
             "--uuid","${auth_uuid}","--accessToken","${auth_access_token}","--userType","${user_type}"]}}
            """), new JsonVersionParserOptions());
        var options = new MLaunchOption
        {
            Path = new MinecraftPath("."), StartVersion = version, JavaPath = "java",
            NativesDirectory = ".", Session = session
        };
        return new MinecraftProcessBuilder(new RulesEvaluator(),
            new RulesEvaluatorContext(LauncherOSRule.Current), options).BuildArguments();
    }

    private static void RejectLocal(Action<MSession> mutation, Action<bool, string> check, string description)
    {
        var session = LocalAccountIdentity.CreateSession("Local_Player");
        mutation(session);
        Rejects(() => LaunchSessionPolicy.CopyForLaunch(session), check, description);
    }

    private static void Rejects(Action action, Action<bool, string> check, string description)
    {
        try { action(); }
        catch (InvalidOperationException) { check(true, description); return; }
        throw new InvalidOperationException("Expected InvalidOperationException: " + description);
    }
}
