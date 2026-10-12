using System.Text.Json.Nodes;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Xray 26.10 refuses repeated routing rule tags ("app/router: duplicate ruleTag").</summary>
public class ColituXrayRuleTagsTests
{
    private static List<string?> Tags(string config) =>
        JsonNode.Parse(config)!["routing"]!["rules"]!.AsArray().Select(rule => rule?["ruleTag"]?.GetValue<string>()).ToList();

    [Fact]
    public void RepeatedTags_BecomeUnique_FirstKeepsItsTag()
    {
        const string config = """
        {"routing":{"rules":[
          {"ruleTag":"colitu-ru-direct","domain":["geosite:category-ru"],"outboundTag":"direct"},
          {"ruleTag":"colitu-ru-direct","ip":["geoip:ru"],"outboundTag":"direct"},
          {"ruleTag":"colitu-ru-direct-2","ip":["198.51.100.0/24"],"outboundTag":"direct"},
          {"ruleTag":"colitu-ru-direct","port":"53","outboundTag":"proxy"},
          {"inboundTag":["colitu-check"],"outboundTag":"proxy"}
        ]}}
        """;
        Tags(ColituXrayRuleTags.Unique(config)).Should().Equal(
            "colitu-ru-direct", "colitu-ru-direct-3", "colitu-ru-direct-2", "colitu-ru-direct-4", null);
    }

    [Fact]
    public void UniqueTags_ConfigUnchanged()
    {
        const string config = """{"routing":{"rules":[{"ruleTag":"a","outboundTag":"direct"},{"ruleTag":"b","outboundTag":"proxy"},{"outboundTag":"proxy"}]}}""";
        ColituXrayRuleTags.Unique(config).Should().BeSameAs(config);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"outbounds":[]}""")]
    [InlineData("[]")]
    public void NoRulesOrUnreadable_Unchanged(string config)
    {
        ColituXrayRuleTags.Unique(config).Should().BeSameAs(config);
    }
}
