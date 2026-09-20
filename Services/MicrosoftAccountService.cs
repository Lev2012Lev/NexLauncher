using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace NexLauncher.Services;

/// <summary>
/// Browser-based Microsoft authentication. Tokens live only in this process.
/// </summary>
public sealed class MicrosoftAccountService : IAccountService
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private AuthState? _state;
    private MSession? _session;

    public string? PlayerName => _session?.Username;

    public async Task<MSession> SignInAsync(string clientId, CancellationToken cancellationToken)
    {
        var normalizedClientId = ValidateClientId(clientId);
        await _operation.WaitAsync(cancellationToken);
        try
        {
            // BuildApplication intentionally does not register a persistent MSAL cache.
            var application = MsalClientHelper.BuildApplication(normalizedClientId);
            var accounts = new InMemoryXboxGameAccountManager(JEGameAccount.FromSessionStorage);
            var candidate = new AuthState(normalizedClientId, application,
                (JEGameAccount)accounts.NewAccount(), accounts);

            // Closing the browser does not notify MSAL. Bound the wait as well as
            // allowing the launcher Cancel button to cancel it immediately.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var session = await AuthenticateAsync(candidate, interactive: true, timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();

            var previous = _state;
            _state = candidate;
            _session = session;
            if (previous is not null)
                await ClearMsalCacheAsync(previous);
            return session;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<MSession?> RestoreAsync(string clientId, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (_state is null)
                return null;

            if (!Guid.TryParse(clientId?.Trim(), out var parsedClientId) ||
                !string.Equals(_state.ClientId, parsedClientId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var previous = _state;
                _state = null;
                _session = null;
                await ClearMsalCacheAsync(previous);
                return null;
            }

            try
            {
                // Refresh Microsoft/Xbox/Minecraft tokens and recheck ownership
                // before launching. This method never opens a browser.
                var session = await AuthenticateAsync(_state, interactive: false, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _session = session;
                return session;
            }
            catch (MsalUiRequiredException)
            {
                var previous = _state;
                _state = null;
                _session = null;
                await ClearMsalCacheAsync(previous);
                return null;
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task SignOutAsync()
    {
        await _operation.WaitAsync();
        try
        {
            var previous = _state;
            _state = null;
            _session = null;
            if (previous is not null)
                await ClearMsalCacheAsync(previous);
        }
        finally
        {
            _operation.Release();
        }
    }

    private static string ValidateClientId(string clientId)
    {
        if (!Guid.TryParse(clientId?.Trim(), out var parsedClientId) || parsedClientId == Guid.Empty)
            throw new InvalidOperationException(
                "Укажи Application (client) ID своего приложения Microsoft в настройках. Инструкция: docs/MICROSOFT_AUTH.md.");
        return parsedClientId.ToString();
    }

    private static async Task<MSession> AuthenticateAsync(
        AuthState state, bool interactive, CancellationToken cancellationToken)
    {
        // Some CmlLib HTTP steps do not forward their context cancellation token.
        // Attach it at the HTTP boundary so Cancel also stops those requests.
        using var http = new HttpClient(new CancellationAwareHandler(cancellationToken))
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        var handler = new JELoginHandlerBuilder()
            .WithHttpClient(http)
            .WithAccountManager(state.Accounts)
            .WithOAuthProvider(new MsalCodeFlowProvider(state.Application))
            .Build();
        var authenticator = handler.CreateAuthenticator(state.Account, cancellationToken);
        authenticator.AddMsalOAuth(state.Application, msal => interactive
            ? msal.Interactive(builder => builder.WithUseEmbeddedWebView(false).WithPrompt(Prompt.SelectAccount))
            : msal.Silent());
        authenticator.AddForceXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddForceJEAuthenticator(je => je.WithGameOwnershipChecker().Build());

        MSession session;
        try
        {
            session = await authenticator.ExecuteForLauncherAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MsalUiRequiredException) when (!interactive)
        {
            throw;
        }
        catch (MsalClientException exception) when (
            exception.ErrorCode is "authentication_canceled" or "user_canceled" or "user_cancelled")
        {
            throw new OperationCanceledException("Вход в Microsoft отменён.", cancellationToken);
        }
        catch (JEAuthException exception) when (exception.StatusCode == 403)
        {
            throw new InvalidOperationException(
                "Minecraft API отклонил вход (403). Проверь допуск своего Client ID к API Minecraft: docs/MICROSOFT_AUTH.md.");
        }
        catch (JEAuthException exception) when (exception.StatusCode == 404 ||
            exception.Message.Contains("doesn't own", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "У этого аккаунта не найден доступ к Minecraft: Java Edition или игровой профиль. Проверь покупку/подписку и создай профиль на minecraft.net.");
        }
        catch (MsalException)
        {
            throw new InvalidOperationException(
                "Microsoft не завершил вход. Проверь Client ID, тип личных аккаунтов и redirect URI http://localhost в регистрации приложения.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "Не удалось связаться с серверами входа. Проверь интернет и повтори попытку.");
        }
        catch (Exception)
        {
            // Do not send third-party exception bodies (which may contain
            // authentication data) to the UI or to the launcher log.
            throw new InvalidOperationException(
                "Не удалось подтвердить аккаунт Microsoft/Xbox/Minecraft. Проверь Xbox-профиль, семейные ограничения и доступ к Java Edition.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(session.Username) || string.IsNullOrWhiteSpace(session.UUID) ||
            string.IsNullOrWhiteSpace(session.AccessToken) || session.UserType != "msa")
            throw new InvalidOperationException("Сервер не вернул действительный профиль Minecraft: Java Edition.");
        return session;
    }

    private static async Task ClearMsalCacheAsync(AuthState state)
    {
        state.Accounts.ClearAccounts();
        await MsalClientHelper.RemoveAccounts(state.Application);
    }

    private sealed record AuthState(
        string ClientId,
        IPublicClientApplication Application,
        JEGameAccount Account,
        InMemoryXboxGameAccountManager Accounts);

    private sealed class CancellationAwareHandler(CancellationToken operationToken) : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationToken, cancellationToken);
            return await base.SendAsync(request, linked.Token);
        }
    }
}
