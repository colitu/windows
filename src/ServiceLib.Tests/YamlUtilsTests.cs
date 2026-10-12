using AwesomeAssertions;
using ServiceLib.Common;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>YamlUtils keeps its behaviour across YamlDotNet major versions (16 → 18).</summary>
public class YamlUtilsTests
{
    public class Sample
    {
        public string? Name { get; set; }
        public int Port { get; set; }
        public List<string>? Tags { get; set; }
    }

    [Fact]
    public void FromYaml_PascalCaseKeys()
    {
        var sample = YamlUtils.FromYaml<Sample>("Name: node\nPort: 443\nTags: [a, b]\n");
        sample.Name.Should().Be("node");
        sample.Port.Should().Be(443);
        sample.Tags.Should().Equal("a", "b");
    }

    [Fact]
    public void ToYaml_HyphenatedKeys()
    {
        var yaml = YamlUtils.ToYaml(new Dictionary<string, object> { ["mixed-port"] = 7890, ["allow-lan"] = false });
        yaml.Should().Contain("mixed-port: 7890").And.Contain("allow-lan: false");
        YamlUtils.ToYaml(null).Should().BeEmpty();
    }

    [Fact]
    public void PreprocessYaml_ResolvesMergeKeys()
    {
        const string source = """
            base: &base
              type: socks5
              port: 1080
            proxy:
              <<: *base
              name: p1
            """;
        var merged = YamlUtils.PreprocessYaml(source)!;
        merged.Should().NotContain("<<");
        merged.Should().Contain("name: p1");
        merged.Split("proxy:")[1].Should().Contain("type: socks5").And.Contain("port: 1080");
    }

    [Fact]
    public void BrokenYaml_ReturnsNullOrDefault()
    {
        YamlUtils.PreprocessYaml("a: [unclosed").Should().BeNull();
        YamlUtils.FromYaml<Sample>("Port: [not-a-number").Should().BeNull();
    }
}
