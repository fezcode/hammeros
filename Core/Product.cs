namespace HammerOS.Core;

/// <summary>
/// The single place the product name and release version live in code.
/// <c>version.ps1</c> rewrites <see cref="Version"/> here alongside
/// HammerOS.csproj and forge.toml, and refuses to release when they disagree,
/// so nothing else in the app may hard-code a version number.
/// </summary>
public static class Product
{
    public const string Name = "HammerOS";
    public const string Version = "0.3.5";

    /// <summary>"HammerOS 1.2.3" — what the CLI, shell and seeded files print.</summary>
    public static string NameAndVersion => $"{Name} {Version}";
}
