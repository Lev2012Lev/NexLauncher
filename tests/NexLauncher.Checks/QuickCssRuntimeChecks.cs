using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NexLauncher.Models;
using NexLauncher.Services.QuickCss;

internal static class QuickCssRuntimeChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "quickcss-runtime");
        Directory.CreateDirectory(directory);
        var css = Path.Combine(directory, "theme.css");
        var button = new Button { Content = "Play", Classes = { "primary", "qc-play-button" } };
        var input = new TextBox { Text = "Text" };
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        var item = new ListBoxItem { Content = "Selected profile" };
        var sidebar = new Border { Classes = { "qc-sidebar" }, Child = new TextBlock { Text = "Sidebar" } };
        var content = new StackPanel { Children = { button, input, combo, item, sidebar } };
        var window = new Window { Classes = { "qc-app" }, Width = 400, Height = 360, Content = content };
        var other = new Window { Classes = { "qc-app" }, Width = 100, Height = 100, Content = new Button { Content = "Other" } };
        window.Show();
        other.Show();
        Dispatcher.UIThread.RunJobs();
        var originalBackground = window.Background;
        var otherBackground = other.Background;
        var originalAccentExists = Application.Current!.Resources.TryGetValue("NxAccentColor", out var originalAccent);
        var originalBrushExists = Application.Current.Resources.TryGetValue("NxAccentBrush", out var originalBrush);
        using var service = new QuickCssService(window);
        var events = 0;
        service.Changed += (_, _) => events++;
        await File.WriteAllTextAsync(css, """
            :root { background-color: #112233; accent-color: #ff88aa; }
            .primary { background-color: #223344; }
            button { background-color: #334455; color: white; }
            button:hover { background-color: #aa1122; }
            .primary:disabled { background-color: #443322; color: #999999; }
            textbox:hover { background-color: #445566; border-color: #8899aa; }
            combobox:hover { background-color: #667788; border-color: #aabbcc; }
            combobox:disabled { background-color: #553311; color: #decaab; }
            list-item:selected { background-color: #884466; color: #f0ddee; }
            #sidebar { background-color: #192837; padding: 8px 12px; }
            """);
        var result = await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = css, AutoReload = false });
        Dispatcher.UIThread.RunJobs();
        check(result.IsApplied && result.Diagnostics.Count == 0, "Quick CSS valid theme applies without diagnostics");
        check(HasColor(window.Background, "#112233"), "Quick CSS window-owned styles affect the window itself");
        check(Equals(other.Background, otherBackground), "Quick CSS styles are scoped to the chosen window");
        check(HasColor(button.Background, "#334455"), "Quick CSS later type rule wins over earlier semantic class rule");
        check(HasColor(sidebar.Background, "#192837") && sidebar.Padding == new Thickness(12, 8), "Quick CSS semantic class maps colors and box values");
        check(Application.Current.Resources["NxAccentColor"] is Color c && c == Color.Parse("#ff88aa"), "Quick CSS accent updates application resources");
        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        ((IPseudoClasses)input.Classes).Set(":pointerover", true);
        ((IPseudoClasses)combo.Classes).Set(":pointerover", true);
        item.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First(x => x.Name == "PART_ContentPresenter");
        var inputBorder = input.GetVisualDescendants().OfType<Border>().First(x => x.Name == "PART_BorderElement");
        var comboBorder = combo.GetVisualDescendants().OfType<Border>().First(x => x.Name == "Background");
        var itemPresenter = item.GetVisualDescendants().OfType<ContentPresenter>().First(x => x.Name == "PART_ContentPresenter");
        check(HasColor(presenter.Background, "#aa1122"), "Quick CSS hover overrides Fluent Button template surface");
        check(HasColor(inputBorder.Background, "#445566") && HasColor(inputBorder.BorderBrush, "#8899aa"), "Quick CSS hover overrides Fluent TextBox border surface");
        check(HasColor(comboBorder.Background, "#667788"), "Quick CSS hover overrides Fluent ComboBox surface");
        check(HasColor(itemPresenter.Background, "#884466"), "Quick CSS selected list item overrides Fluent template surface");
        button.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        check(HasColor(presenter.Background, "#443322"), "Quick CSS disabled rule overrides Fluent Button template");
        combo.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        var comboContent = combo.GetVisualDescendants().OfType<ContentControl>().First(x => x.Name == "ContentPresenter");
        var comboGlyph = combo.GetVisualDescendants().OfType<PathIcon>().First(x => x.Name == "DropDownGlyph");
        check(HasColor(comboContent.Foreground, "#decaab") && HasColor(comboGlyph.Foreground, "#decaab"), "Quick CSS disabled ComboBox color reaches text and glyph template parts");
        using (var frame = window.CaptureRenderedFrame())
        {
            check(frame is not null, "Quick CSS styled controls produce a rendered frame");
            frame!.Save(Path.Combine(directory, "styled-controls.png"), PngBitmapEncoderOptions.Default);
        }
        await File.WriteAllTextAsync(css, "button:hover { background: #110022; } button { background: #991122; background-color: #223344; background: #445577; }");
        button.IsEnabled = true;
        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = css, AutoReload = false });
        Dispatcher.UIThread.RunJobs();
        check(HasColor(button.Background, "#445577") && HasColor(presenter.Background, "#445577"), "Quick CSS source order is consistent for active states and background aliases");
        await service.ConfigureAsync(new QuickCssSettings());
        Dispatcher.UIThread.RunJobs();
        check(Equals(window.Background, originalBackground), "Quick CSS disable restores original window styles");
        check(Application.Current.Resources.TryGetValue("NxAccentColor", out var restoredAccent) == originalAccentExists && Equals(restoredAccent, originalAccent), "Quick CSS restores exact original accent color resource");
        check(Application.Current.Resources.TryGetValue("NxAccentBrush", out var restoredBrush) == originalBrushExists && ReferenceEquals(restoredBrush, originalBrush), "Quick CSS restores original accent brush object");

        await File.WriteAllTextAsync(css, "#app { background-color: #203040; invalid-property: 1; opacity: .9; } #content { border-width: 1; }");
        result = await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = css, AutoReload = false });
        check(result.IsApplied && result.Diagnostics.Count == 2 && HasColor(window.Background, "#203040"), "Quick CSS ignores invalid declarations while valid siblings apply");
        await File.WriteAllTextAsync(css, new string('x', 128 * 1024 + 1));
        result = await service.ReloadAsync();
        check(result.IsApplied && result.Diagnostics.Count > 0 && HasColor(window.Background, "#203040"), "Quick CSS oversized file preserves last good overlay");
        File.Delete(css);
        result = await service.ReloadAsync();
        check(result.IsApplied && HasColor(window.Background, "#203040"), "Quick CSS missing file preserves last good overlay");

        await File.WriteAllTextAsync(css, "#app { background-color: #123456; }");
        var first = service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = css, AutoReload = false });
        var latest = service.ConfigureAsync(new QuickCssSettings { Enabled = false, FilePath = css, AutoReload = false });
        try { await first; } catch (OperationCanceledException) { }
        await latest;
        check(Equals(window.Background, originalBackground), "Quick CSS stale load cannot reapply after disable");

        await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = css, AutoReload = true });
        var replacement = Path.Combine(directory, "replacement.tmp");
        await File.WriteAllTextAsync(replacement, "#app { background-color: #654321; }");
        File.Move(replacement, css, overwrite: true);
        await Until(() => HasColor(window.Background, "#654321"));
        check(HasColor(window.Background, "#654321"), "Quick CSS watcher detects atomic editor file replacement");
        var nextCss = Path.Combine(directory, "second.css");
        await File.WriteAllTextAsync(nextCss, "#app { background-color: #776655; }");
        await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = nextCss, AutoReload = true });
        var beforeOldChange = events;
        await File.WriteAllTextAsync(css, "#app { background-color: red; }");
        await Task.Delay(550);
        check(HasColor(window.Background, "#776655") && events == beforeOldChange, "Quick CSS replaces watchers when file selection changes");

        // A real, tiny PNG is created locally and then decoded by the theme worker.
        var imagePath = Path.Combine(directory, "background.png");
        using (var image = new RenderTargetBitmap(new PixelSize(2, 2))) image.Save(imagePath, PngBitmapEncoderOptions.Default);
        await File.WriteAllTextAsync(nextCss, "#app { background-image: url(\"background.png\"); background-size: contain; background-position: top; background-opacity: .3; }");
        await service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = nextCss, AutoReload = false });
        check(window.Background is ImageBrush { Stretch: Stretch.Uniform, AlignmentY: AlignmentY.Top } imageBrush && Math.Abs(imageBrush.Opacity - .3) < .001, "Quick CSS loads bounded local image and brush-only options");
        foreach (var invalid in new[] { "../background.png", "https://example.test/image.png", imagePath.Replace('\\', '/') })
        {
            await File.WriteAllTextAsync(nextCss, "#app { background-color: #102030; background-image: url(\"" + invalid + "\"); }");
            result = await service.ReloadAsync();
            check(result.Diagnostics.Count > 0 && HasColor(window.Background, "#102030"), "Quick CSS rejects unsafe image path: " + invalid);
        }
        var bomb = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bomb, 0);
        "IHDR"u8.CopyTo(bomb.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bomb.AsSpan(16), 100000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bomb.AsSpan(20), 100000);
        await File.WriteAllBytesAsync(imagePath, bomb);
        await File.WriteAllTextAsync(nextCss, "#app { background-image: url(\"background.png\"); }");
        result = await service.ReloadAsync();
        check(result.Diagnostics.Any(x => x.Message.Contains("4096")), "Quick CSS rejects oversized image dimensions before decoding");
        await File.WriteAllTextAsync(nextCss, "#app { background-color: #ddeeaa; }");
        var pending = service.ConfigureAsync(new QuickCssSettings { Enabled = true, FilePath = nextCss, AutoReload = true });
        service.Dispose();
        try { await pending; } catch (OperationCanceledException) { }
        var eventsAtDispose = events;
        await File.WriteAllTextAsync(nextCss, "#app { background-color: #aaddee; }");
        await Task.Delay(550);
        check(Equals(window.Background, originalBackground) && eventsAtDispose == events, "Quick CSS dispose removes overlay and suppresses stale/watch callbacks");
        window.Close();
        other.Close();
    }

    private static bool HasColor(IBrush? brush, string value) => brush is ISolidColorBrush solid && solid.Color == Color.Parse(value);
    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException("Quick CSS watcher did not apply the new theme.");
            await Task.Delay(30);
        }
    }
}
