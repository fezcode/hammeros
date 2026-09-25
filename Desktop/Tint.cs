using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace HammerOS.Desktop;

// Accent colors that follow the desktop palette. Windows keep their cream paper; the palette
// tints title bars, dark screens (Refinement, terminals, Sudoku), bars, hovers and the accent.
// Every brush is one shared instance recolored in place, so open windows follow a palette change
// without being rebuilt. App.axaml reaches the same instances as DynamicResource "Hmr.*" keys.
public static class Tint
{
    private sealed record Palette(string Dark, string Bar, string DarkLine, string DarkRaised, string DarkHover, string Primary, string Accent, string AccentSoft, string TitleActive, string TitleInactive, string BorderActive, string BorderInactive, string Fluent);
    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["Petrol"] = new("#102C32", "#18363D", "#35545A", "#24464A", "#31575B", "#214B4D", "#54796A", "#D4DECE", "#E9EBDD", "#CCD6C9", "#BDCCBC", "#82968A", "#416C5D"),
        ["Evergreen"] = new("#0F2A22", "#17392E", "#345A4B", "#22483A", "#2E5A48", "#1F4D3C", "#4F7F62", "#D2E0CC", "#E8ECDA", "#CBD8C4", "#BACDB5", "#7F9A84", "#3F6E52"),
        ["Midnight"] = new("#0E1E2B", "#162B3B", "#334C5E", "#213A4E", "#2D4A60", "#1F3C55", "#52708A", "#D2DAE0", "#E6E9E4", "#C8D0D4", "#B8C6CE", "#7D8F9C", "#3F5E7A"),
        ["Graphite"] = new("#15191A", "#1F2425", "#3D4546", "#2A3132", "#363E3F", "#2D3536", "#6B7677", "#D8DAD5", "#E8E8E1", "#CDCFC9", "#C0C3BD", "#8A9090", "#525C5D"),
    };
    public static readonly SolidColorBrush Dark = new(), Bar = new(), DarkLine = new(), DarkRaised = new(), DarkHover = new(), Primary = new(),
        Accent = new(), AccentSoft = new(), TitleActive = new(), TitleInactive = new(), BorderActive = new(), BorderInactive = new();
    public static string Current { get; private set; } = "";
    static Tint() => Apply("Petrol");

    public static void Register(IResourceDictionary resources)
    {
        foreach (var (key, brush) in new[] { ("Dark", Dark), ("Bar", Bar), ("DarkLine", DarkLine), ("DarkRaised", DarkRaised), ("DarkHover", DarkHover), ("Primary", Primary), ("Accent", Accent), ("AccentSoft", AccentSoft) })
            resources["Hmr." + key] = brush;
    }
    // Returns false when the palette is already applied.
    public static bool Apply(string name)
    {
        if (!Palettes.TryGetValue(name, out var palette)) { name = "Petrol"; palette = Palettes[name]; }
        if (name == Current) return false; Current = name;
        Dark.Color = Color.Parse(palette.Dark); Bar.Color = Color.Parse(palette.Bar); DarkLine.Color = Color.Parse(palette.DarkLine);
        DarkRaised.Color = Color.Parse(palette.DarkRaised); DarkHover.Color = Color.Parse(palette.DarkHover); Primary.Color = Color.Parse(palette.Primary);
        Accent.Color = Color.Parse(palette.Accent); AccentSoft.Color = Color.Parse(palette.AccentSoft);
        TitleActive.Color = Color.Parse(palette.TitleActive); TitleInactive.Color = Color.Parse(palette.TitleInactive);
        BorderActive.Color = Color.Parse(palette.BorderActive); BorderInactive.Color = Color.Parse(palette.BorderInactive);
        // Toggle switches, focus rings and other Fluent controls take the palette's accent too.
        if (Application.Current?.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent && fluent.Palettes.TryGetValue(ThemeVariant.Light, out var fluentPalette))
            fluentPalette.Accent = Color.Parse(palette.Fluent);
        return true;
    }
}
