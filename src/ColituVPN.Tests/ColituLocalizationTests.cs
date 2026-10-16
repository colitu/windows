using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituLocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{[a-z]+\}", RegexOptions.Compiled);

    [Fact]
    public void EveryKey_HasTextInEveryLanguage()
    {
        foreach (var key in Loc.Keys)
        {
            var values = Loc.ValuesOf(key);
            values.Should().HaveCount(Loc.Languages.Length, key);
            values.Should().OnlyContain(value => !string.IsNullOrWhiteSpace(value), key);
        }
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        foreach (var key in Loc.Keys)
        {
            var sets = Loc.ValuesOf(key)
                .Select(value => string.Join(",", Placeholder.Matches(value).Select(match => match.Value).OrderBy(x => x)))
                .Distinct()
                .ToList();
            sets.Should().HaveCount(1, $"{key} must use the same placeholders in every language");
        }
    }

    [Fact]
    public void EveryKeyUsedByTheApp_Exists()
    {
        var root = FindAppSource();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{s:T ([a-zA-Z.]+)\}"))
            {
                used.Add(match.Groups[1].Value);
            }
        }
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"(?:Loc\.I|loc)\[""([a-zA-Z.]+)""\]|Format\(""([a-zA-Z.]+)""|Notice\?\.Invoke\(""([a-zA-Z.]+)""\)"))
            {
                used.Add(match.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value);
            }
            foreach (Match match in Regex.Matches(text, @"Count\(""([a-z]+)"""))
            {
                used.Add(match.Groups[1].Value + ".one");
                used.Add(match.Groups[1].Value + ".few");
                used.Add(match.Groups[1].Value + ".many");
            }
        }

        used.Should().NotBeEmpty();
        used.Where(key => !Loc.Has(key)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "1 день")]
    [InlineData(2, "2 дня")]
    [InlineData(5, "5 дней")]
    [InlineData(11, "11 дней")]
    [InlineData(21, "21 день")]
    [InlineData(22, "22 дня")]
    [InlineData(112, "112 дней")]
    public void Russian_UsesTheRightPluralForm(long n, string expected)
    {
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage("ru");
            Loc.I.Count("day", n).Should().Be(expected);
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    [Fact]
    public void UnknownLanguage_FallsBackToADefault()
    {
        Loc.Normalize("de").Should().BeOneOf("ru", "tr", "en", "zh", "fa");
        Loc.Normalize(" ZH ").Should().Be("zh");
        Loc.Normalize("fa").Should().Be("fa");
        Loc.Normalize(" TR ").Should().Be("tr");
    }

    [Fact]
    public void Persian_IsRightToLeft_OthersAreNot()
    {
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage("fa");
            Loc.I.IsRightToLeft.Should().BeTrue();
            Loc.I.FlowDirection.Should().Be(System.Windows.FlowDirection.RightToLeft);
            Loc.I["nav.home"].Should().NotBeNullOrWhiteSpace();
            Loc.I.Count("day", 3).Should().Be("3 روز");
            Loc.I.SetLanguage("zh");
            Loc.I.IsRightToLeft.Should().BeFalse();
            Loc.I.Count("day", 3).Should().Be("3 天");
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    private static string FindAppSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "ColituVPN", "Views")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the tests run inside the repository");
        return Path.Combine(directory!.FullName, "ColituVPN");
    }
}
