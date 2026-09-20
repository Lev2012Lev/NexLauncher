using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using NexLauncher.Models;

namespace NexLauncher.Services;

public interface IMinecraftService
{
    Task<IReadOnlyList<MinecraftRelease>> GetVersionsAsync(CancellationToken cancellationToken);
    bool IsInstalled(GameInstance instance);
    Task InstallAsync(GameInstance instance, IProgress<LaunchProgress> progress, CancellationToken cancellationToken);
    Task<int> LaunchAsync(GameInstance instance, MSession session, IProgress<LaunchProgress> progress,
        Action<string> log, CancellationToken cancellationToken);
}
