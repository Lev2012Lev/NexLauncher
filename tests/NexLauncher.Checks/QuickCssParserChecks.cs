using System.Globalization;
using NexLauncher.Services.QuickCss;

internal static class QuickCssParserChecks
{
    public static void Run(Action<bool, string> check)
    {
        var ordinary = QuickCssParser.Parse("/* theme */\n:root { --accent: #8af; accent-color: var(--accent); }\nButton.primary, #play-button { color: white; padding: 8px 16px; }");
        check(ordinary.Diagnostics.Count == 0 && ordinary.Rules.Count == 2, "Quick CSS parses comments, root and selector lists");
        check(ordinary.Rules[0].Declarations.Single().Value == "#8af" && ordinary.Rules[1].Selectors.Count == 2,
            "Quick CSS resolves variables and preserves selector/source order");
        check(ordinary.Rules[1].Line == 3 && ordinary.Rules[1].Declarations[0].Line == 3, "Quick CSS retains source line numbers");

        var variables = QuickCssParser.Parse("""
            :root { --base: #123; --text: var(--base); }
            Button { color: var(--text); background: var(--missing, rgba(1, 2, 3, .5)); }
            :root { --base: #456; }
            #sidebar { border-color: var(--missing, var(--base)); }
            """);
        check(variables.Diagnostics.Count == 0 && variables.Rules[0].Declarations[0].Value == "#456",
            "Quick CSS uses final root variable definitions and recursive references");
        check(variables.Rules[0].Declarations[1].Value == "rgba(1, 2, 3, .5)" && variables.Rules[1].Declarations[0].Value == "#456",
            "Quick CSS handles comma-containing and nested variable fallbacks");
        var cycles = QuickCssParser.Parse(":root { --a: var(--b); --b: var(--a); --safe: var(--a, blue); } Button { color: var(--a); background: var(--a, white); border-color: var(--safe); opacity: 1; }");
        check(cycles.Diagnostics.Count == 1 && cycles.Rules.Single().Declarations.Count == 3,
            "Quick CSS isolates variable cycles while allowing usable outside fallbacks");
        check(cycles.Rules[0].Declarations[1].Value == "blue", "Quick CSS does not incorrectly invalidate dependencies outside a cycle");
        var scoped = QuickCssParser.Parse("Button { --local: red; color: var(--local); opacity: 1; } :root, Button { --bad: blue; }");
        check(scoped.Diagnostics.Count == 3 && scoped.Rules.Single().Declarations.Single().Property == "opacity",
            "Quick CSS rejects unsupported variable scope without dropping neighboring declarations");
        var caseSensitive = QuickCssParser.Parse(":root { --Color: red; } Button { color: var(--color, blue); }");
        check(caseSensitive.Rules.Single().Declarations.Single().Value == "blue", "Quick CSS variable names are case sensitive");

        var damaged = QuickCssParser.Parse("Button { broken; opacity: ; color: red !important; padding: rgb(; margin: 2px; }\n#sidebar { opacity: .8; }");
        check(damaged.Diagnostics.Count == 4 && damaged.Rules.Count == 2 && damaged.Rules[0].Declarations.Single().Property == "margin",
            "Quick CSS isolates malformed declarations and keeps neighboring valid rules");
        var nested = QuickCssParser.Parse("@import 'remote.css'; @media screen { Button { opacity: 0; } } .card { Button { opacity: 0; } } #sidebar { opacity: 1; }");
        check(nested.Diagnostics.Count == 3 && nested.Rules.Single().Selectors.Single() == "#sidebar",
            "Quick CSS ignores imports, at-rules and nested blocks safely");
        var selectors = QuickCssParser.Parse("Button:hover, .card:selected, #play-button:disabled { opacity: .7; } Button > TextBlock { color: red; } Button[checked] { color: red; } Button:focus { color: red; } Button:hover:disabled { color: red; }");
        check(selectors.Rules.Count == 1 && selectors.Rules[0].Selectors.Count == 3 && selectors.Diagnostics.Count == 4,
            "Quick CSS permits only documented pseudo states and rejects unsupported selector grammar");
        var unknown = QuickCssParser.Parse("FutureControl { future-property: 12; }");
        check(unknown.Diagnostics.Count == 0 && unknown.Rules.Single().Declarations.Single().Property == "future-property",
            "Quick CSS leaves registry policy outside syntax parser for extensibility");
        var quoted = QuickCssParser.Parse("""
            #sidebar { background-image: url('wall;paper.png'); font-family: "Inter", "Segoe UI"; }
            Button { background-image: url('/*literal*/var(--literal).png'); }
            """);
        check(quoted.Diagnostics.Count == 0 && quoted.Rules[0].Declarations.Count == 2 && quoted.Rules[0].Declarations[0].Value == "url('wall;paper.png')",
            "Quick CSS preserves quoted image paths and font families without evaluation");
        check(quoted.Rules[1].Declarations.Single().Value == "url('/*literal*/var(--literal).png')",
            "Quick CSS does not interpret comments or variables inside strings");
        var unterminated = QuickCssParser.Parse("Button { opacity: 1; }\n/* unfinished");
        check(unterminated.Diagnostics.Single().Line == 2 && unterminated.Rules.Count == 1,
            "Quick CSS reports an unfinished comment and retains previous rules");
        check(QuickCssParser.Parse("Button { opacity: 1;").Rules.Count == 0,
            "Quick CSS ignores incomplete final blocks");
        var unclosedString = QuickCssParser.Parse("Button { font-family: 'broken\n; opacity: 1; } #sidebar { opacity: .5; }");
        check(unclosedString.Diagnostics.Count == 1 && unclosedString.Rules.Count == 2 && unclosedString.Rules[0].Declarations.Single().Property == "opacity",
            "Quick CSS recovers after newline in an unterminated string");

        check(CssValueParser.TryColor("#1aF", out var color) && color == new CssColor(17, 170, 255), "CSS shorthand RGB expands channels");
        check(CssValueParser.TryColor("#1234", out color) && color == new CssColor(17, 34, 51, 68), "CSS shorthand RGBA puts alpha last");
        check(CssValueParser.TryColor("#12345678", out color) && color == new CssColor(18, 52, 86, 120), "CSS eight-digit color uses RGBA rather than Avalonia ARGB");
        check(CssValueParser.TryColor("rgba(255, 10, 0, 0.5)", out color) && color == new CssColor(255, 10, 0, 128), "CSS rgba channels and fractional alpha parse");
        check(CssValueParser.TryColor("RGB(1, 2, 3)", out color) && color == new CssColor(1, 2, 3), "CSS rgb function accepts integer channels");
        check(CssValueParser.TryColor("transparent", out color) && color.A == 0, "CSS transparent has zero alpha");
        check(!CssValueParser.TryColor("rgba(0, 0, 0, 2)", out _) && !CssValueParser.TryColor("#12345", out _)
              && !CssValueParser.TryColor("rgb(256,0,0)", out _) && !CssValueParser.TryColor("rgb(10%,0,0)", out _), "CSS colors reject invalid ranges and unsupported syntax");

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            check(CssValueParser.TryNumber("12.5px", 0, 100, out var number) && number == 12.5, "CSS numbers stay culture invariant");
            check(!CssValueParser.TryNumber("12,5", 0, 100, out _) && !CssValueParser.TryNumber("NaN", 0, 100, out _)
                  && !CssValueParser.TryNumber("Infinity", 0, 100, out _) && !CssValueParser.TryNumber("5%", 0, 100, out _)
                  && !CssValueParser.TryNumber("1e2", 0, 100, out _) && !CssValueParser.TryNumber("-1", 0, 100, out _),
                "CSS numbers reject non-finite, out-of-range and unsupported units");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        check(CssValueParser.TryBox("2px", 100, out var box) && box == new CssBox(2, 2, 2, 2), "CSS one-value box maps all sides");
        check(CssValueParser.TryBox("2px 4px", 100, out box) && box == new CssBox(2, 4, 2, 4), "CSS two-value box maps vertical and horizontal sides");
        check(CssValueParser.TryBox("2 4 6", 100, out box) && box == new CssBox(2, 4, 6, 4), "CSS three-value box follows top, horizontal, bottom order");
        check(CssValueParser.TryBox("1 2 3 4", 100, out box) && box == new CssBox(1, 2, 3, 4), "CSS four-value box follows top-right-bottom-left order");
        check(!CssValueParser.TryBox("-1 2", 100, out _) && !CssValueParser.TryBox("1 2 3 4 5", 100, out _)
              && !CssValueParser.TryBox("101", 100, out _), "CSS boxes reject negative, oversized and excessive values");
        check(CssValueParser.TryFontWeight("bold", out var weight) && weight == 700 && CssValueParser.TryFontWeight("600", out weight) && weight == 600
              && !CssValueParser.TryFontWeight("650", out _) && !CssValueParser.TryFontWeight("1000", out _), "CSS weights support normal/bold and 100–900 steps");

        check(QuickCssParser.Parse(new string('x', QuickCssParser.MaximumInputBytes + 1)).Diagnostics.Count == 1,
            "Quick CSS enforces input length limit");
        check(QuickCssParser.Parse(new string('я', QuickCssParser.MaximumInputBytes / 2 + 1)).Diagnostics.Count == 1,
            "Quick CSS input limit counts UTF-8 bytes");
        var manyRules = QuickCssParser.Parse(string.Concat(Enumerable.Repeat("Button { opacity: 1; }", QuickCssParser.MaximumRules + 1)));
        check(manyRules.Rules.Count == QuickCssParser.MaximumRules && manyRules.Diagnostics.Count == 1, "Quick CSS stops at rule limit");
        var manyDeclarations = QuickCssParser.Parse("Button {" + string.Concat(Enumerable.Repeat("opacity:1;", QuickCssParser.MaximumDeclarations + 1)) + "}");
        check(manyDeclarations.Rules.Single().Declarations.Count == QuickCssParser.MaximumDeclarations && manyDeclarations.Diagnostics.Count == 1,
            "Quick CSS stops at declaration limit");
        var deepValue = QuickCssParser.Parse("Button { color: " + new string('(', 33) + "red" + new string(')', 33) + "; opacity:1; }");
        check(deepValue.Diagnostics.Count == 1 && deepValue.Rules.Single().Declarations.Count == 1, "Quick CSS bounds function nesting");
        var largeValue = QuickCssParser.Parse("Button { font-family: " + new string('a', QuickCssParser.MaximumValueLength + 1) + "; opacity:1; }");
        check(largeValue.Diagnostics.Count == 1 && largeValue.Rules.Single().Declarations.Count == 1, "Quick CSS bounds individual values");
        var deepVariables = ":root { --v0: red; " + string.Concat(Enumerable.Range(1, 19).Select(index => $"--v{index}: var(--v{index - 1});")) + "} Button { color:var(--v19); opacity:1; }";
        var deep = QuickCssParser.Parse(deepVariables);
        check(deep.Diagnostics.Count == 1 && deep.Rules.Single().Declarations.Single().Property == "opacity", "Quick CSS bounds recursive variable depth");
        var amplification = ":root { --v0: missing; " + string.Concat(Enumerable.Range(1, 14).Select(index => $"--v{index}: var(--v{index - 1}) var(--v{index - 1});")) + "} Button { color:var(--v13); opacity:1; }";
        var amplified = QuickCssParser.Parse(amplification);
        check(amplified.Diagnostics.Count == 1 && amplified.Rules.Single().Declarations.Single().Property == "opacity", "Quick CSS bounds expanded variable length");
    }
}
