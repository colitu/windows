using System.IO;
using System.Text;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// colitu-build.json: the mirror and ad-blocking lists a release ships next to the program instead of
/// compiling them in (reproducible builds).
/// </summary>
public class ColituBuildValuesTests
{
    [Fact]
    public void ReadsTheFileBuildInstallerWrites()
    {
        // The exact layout scripts/build-installer.ps1 writes (ConvertTo-Json strings, UTF-8 without BOM, LF).
        var path = Path.Combine(Path.GetTempPath(), $"colitu-build-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"mirrors\":\"https://m1.example.test,https://m2.example.test\",\"adBlockDoh\":\"https://doh.example.test/dns-query\"}\n", new UTF8Encoding(false));
        try
        {
            var values = ColituBuildValues.Load(path);
            ColituBuildValues.Read(values, "mirrors").Should().Be("https://m1.example.test,https://m2.example.test");
            ColituBuildValues.Read(values, "adBlockDoh").Should().Be("https://doh.example.test/dns-query");
            ColituEndpointList.ParseMirrors(ColituBuildValues.Read(values, "mirrors"))
                .Should().Equal("https://m1.example.test", "https://m2.example.test");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NoFileOrABrokenOneGivesNoValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"colitu-build-{Guid.NewGuid():N}.json");
        ColituBuildValues.Load(path).Should().BeNull("a developer build has no file");
        File.WriteAllText(path, "not json");
        try
        {
            ColituBuildValues.Read(ColituBuildValues.Load(path), "mirrors").Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
