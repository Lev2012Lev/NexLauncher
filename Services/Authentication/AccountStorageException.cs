using System;

namespace NexLauncher.Services.Authentication;

/// <summary>A safe, user-facing storage error with no credential-bearing inner exception.</summary>
public sealed class AccountStorageException(string message) : InvalidOperationException(message);
