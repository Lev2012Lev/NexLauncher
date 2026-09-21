using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexLauncher.Models;

namespace NexLauncher.Services.QuickCss;

public sealed record QuickCssResult(bool IsApplied, int RuleCount,
    IReadOnlyList<QuickCssDiagnostic> Diagnostics, string Message);

public interface IQuickCssService : IDisposable
{
    event EventHandler<QuickCssResult>? Changed;
    Task<QuickCssResult> ConfigureAsync(QuickCssSettings settings, CancellationToken cancellationToken = default);
    Task<QuickCssResult> ReloadAsync(CancellationToken cancellationToken = default);
}
