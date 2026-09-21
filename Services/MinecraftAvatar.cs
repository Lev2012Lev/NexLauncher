using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace NexLauncher.Services;

/// <summary>Best-effort avatar from the official profile skin URL, without authentication headers.</summary>
public static class MinecraftAvatar
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<CroppedBitmap?> LoadAsync(string? url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || uri.Host != "textures.minecraft.net" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith("/texture/"))
            return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            token = timeout.Token;
            uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024) return null;
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (bytes.Length + count > 256 * 1024) return null;
                await bytes.WriteAsync(buffer.AsMemory(0, count), token);
            }
            // Decode at a fixed width rather than trusting arbitrary source dimensions.
            bytes.Position = 0;
            var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(bytes, 64), token);
            if (bitmap.PixelSize.Height is not (32 or 64)) { bitmap.Dispose(); return null; }
            return new CroppedBitmap { Source = bitmap, SourceRect = new PixelRect(8, 8, 8, 8) };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
