using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NexLauncher.Services.QuickCss;

/// <summary>Reads bounded local theme assets and decodes them before dispatching UI work.</summary>
internal static class QuickCssImageLoader
{
    internal static async Task<PreparedQuickCss> PrepareAsync(QuickCssDocument document, string cssPath, CancellationToken token)
    {
        var diagnostics = new List<QuickCssDiagnostic>(document.Diagnostics);
        var images = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        long totalPixels = 0;
        var attemptedImages = new HashSet<string>(StringComparer.Ordinal);
        var failedImages = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var declaration in document.Rules.SelectMany(x => x.Declarations).Where(x => x.Property == "background-image"))
            {
                token.ThrowIfCancellationRequested();
                var value = declaration.Value;
                if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || images.ContainsKey(value)) continue;
                if (failedImages.TryGetValue(value, out var previousFailure))
                {
                    diagnostics.Add(new(declaration.Line, previousFailure));
                    continue;
                }
                try
                {
                    if (attemptedImages.Count >= 4) throw new InvalidDataException("В теме допускается не более 4 фоновых изображений.");
                    attemptedImages.Add(value);
                    var path = ResolveImagePath(cssPath, value);
                    var bytes = await ReadBoundedAsync(path, 8 * 1024 * 1024, token).ConfigureAwait(false);
                    var (width, height) = ReadDimensions(bytes);
                    if (width is < 1 or > 4096 || height is < 1 or > 4096 || totalPixels + (long)width * height > 16_777_216)
                        throw new InvalidDataException("Фоны ограничены 4096 px по стороне и 16 мегапикселями на тему.");
                    using var imageStream = new MemoryStream(bytes, writable: false);
                    var bitmap = new Bitmap(imageStream);
                    images.Add(value, bitmap);
                    totalPixels += (long)width * height;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
                { failedImages[value] = ex.Message; diagnostics.Add(new(declaration.Line, ex.Message)); }
            }
            return new(document, images, diagnostics);
        }
        catch
        {
            foreach (var image in images.Values) image.Dispose();
            throw;
        }
    }

    private static string ResolveImagePath(string cssPath, string value)
    {
        var input = value.Trim();
        if (!input.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !input.EndsWith(')'))
            throw new InvalidDataException("background-image: ожидается url(\"image.png\") или none.");
        var relative = QuickCssStyleApplier.Unquote(input[4..^1].Trim());
        if (relative.Length == 0 || relative.Contains(':') || relative.Contains('\0') || Path.IsPathRooted(relative) ||
            relative.Split('/', '\\').Any(x => x is ".." or "." or ""))
            throw new InvalidDataException("Фон должен находиться внутри папки темы; URL, абсолютные пути и переходы .. запрещены.");
        var root = Path.GetFullPath(Path.GetDirectoryName(cssPath)!);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Фон выходит за пределы папки темы.");
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Сетевые пути для фонов не поддерживаются.");
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Поддерживаются только PNG и JPEG из папки темы.");
        for (var part = path; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Символические ссылки и точки перенаправления для фонов не поддерживаются.");
        return path;
    }

    private static (int Width, int Height) ReadDimensions(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8)
        {
            var offset = 2;
            while (offset + 4 <= bytes.Length)
            {
                if (bytes[offset++] != 0xff) break;
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) break;
                var marker = bytes[offset++];
                if (marker is 0xd9 or 0xda) break;
                if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
                if (offset + 2 > bytes.Length) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
                if (length < 2 || offset + length > bytes.Length) break;
                if (marker is 0xc0 or 0xc1 or 0xc2 && length >= 7)
                    return (BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2)));
                offset += length;
            }
        }
        throw new InvalidDataException("Фон не является поддерживаемым PNG или JPEG.");
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path, int limit, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous);
        if (stream.Length > limit) throw new InvalidDataException($"Файл превышает ограничение {limit / 1024} КиБ.");
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > limit) throw new InvalidDataException($"Файл превышает ограничение {limit / 1024} КиБ.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

}

internal sealed class PreparedQuickCss : IDisposable
{
    private bool _ownsImages = true;
    public PreparedQuickCss(QuickCssDocument document, IReadOnlyDictionary<string, Bitmap> images, IReadOnlyList<QuickCssDiagnostic> diagnostics)
    { Document = document; Images = images; Diagnostics = diagnostics; }
    public QuickCssDocument Document { get; }
    public IReadOnlyDictionary<string, Bitmap> Images { get; }
    public IReadOnlyList<QuickCssDiagnostic> Diagnostics { get; }
    public List<Bitmap> TakeImages() { _ownsImages = false; return Images.Values.ToList(); }
    public void Dispose()
    {
        if (!_ownsImages) return;
        _ownsImages = false;
        foreach (var image in Images.Values) image.Dispose();
    }
}
