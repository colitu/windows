using System.Text.Json;

namespace v2rayN.Services;

/// <summary>
/// Values a release gets from outside the public source: the API mirror origins and the ad-blocking
/// DoH servers (both comma separated). scripts/build-installer.ps1 writes them to colitu-build.json
/// next to ColituVPN.exe, in the admin-only install folder, instead of compiling them into the
/// program: the executables then come out byte for byte the same from the public source
/// (reproducible builds), and only this one file differs from a build without the values.
/// No file (a developer build): no mirrors and no built-in ad-blocking list, as before.
/// </summary>
internal static class ColituBuildValues
{
    public const string FileName = "colitu-build.json";

    private static readonly Lazy<JsonElement?> Values = new(() => Load(Path.Combine(AppContext.BaseDirectory, FileName)));

    /// <summary>Comma-separated https origins of the API mirrors, or null.</summary>
    public static string? Mirrors => Read(Values.Value, "mirrors");

    /// <summary>Comma-separated ad-blocking DoH URLs, or null.</summary>
    public static string? AdBlockDoh => Read(Values.Value, "adBlockDoh");

    internal static string? Read(JsonElement? values, string name) =>
        values is { } root && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static JsonElement? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
