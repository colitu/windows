using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituServerPicksTests
{
    [Fact]
    public void AddRecent_PutsTheNewestFirst_KeepsThree_NoRepeats()
    {
        var recent = ColituServerPicks.AddRecent(null, "de");
        recent = ColituServerPicks.AddRecent(recent, "fi");
        recent = ColituServerPicks.AddRecent(recent, "se");
        recent.Should().Equal("se", "fi", "de");

        // Connecting to one already in the list moves it to the top instead of adding it twice.
        ColituServerPicks.AddRecent(recent, "DE").Should().Equal("DE", "se", "fi");
        // A fourth server pushes the oldest out.
        ColituServerPicks.AddRecent(recent, "uk").Should().Equal("uk", "se", "fi");
        ColituServerPicks.AddRecent(recent, " ").Should().Equal("se", "fi", "de");
    }

    [Fact]
    public void ToggleFavorite_AddsFirst_AndRemovesAgain()
    {
        var favorites = ColituServerPicks.ToggleFavorite(["de"], "fi", out var added);
        added.Should().BeTrue();
        favorites.Should().Equal("fi", "de");

        favorites = ColituServerPicks.ToggleFavorite(favorites, "DE", out added);
        added.Should().BeFalse();
        favorites.Should().Equal("fi");
    }

    [Fact]
    public void Normalize_DropsEmptyAndRepeatedIds_AndCaps()
    {
        ColituServerPicks.Normalize(["a", " ", null, "A", "b", "c"], 2).Should().Equal("a", "b");
        ColituServerPicks.Normalize(null, 3).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_LeavesOutServersThePanelNoLongerLists()
    {
        var listed = new Dictionary<string, string> { ["de"] = "Germany", ["fi"] = "Finland" };
        ColituServerPicks.Resolve(["gone", "fi", "de"], id => listed.GetValueOrDefault(id))
            .Should().Equal("Finland", "Germany");
    }

    [Fact]
    public void Shortcuts_FavouritesFirst_ThenRecent_OnlyUsable_AtMostThree()
    {
        var usable = new HashSet<string> { "de", "fi", "se", "uk", "pl" };
        ColituServerPicks.Shortcuts(["fi", "deleted"], ["de", "fi", "se", "uk"], usable.Contains)
            .Should().Equal("fi", "de", "se");
        ColituServerPicks.Shortcuts([], [], usable.Contains).Should().BeEmpty();
        // A deleted (or offline) server never shows; the list is then empty and Home hides the row.
        ColituServerPicks.Shortcuts(["deleted"], ["offline"], usable.Contains).Should().BeEmpty();
    }

    [Fact]
    public void Preferences_NormalizeKeepsTheListsTidy()
    {
        var preferences = new ColituVpnPreferences(
            FavoriteServerIds: ["de", "DE", "", "fi"],
            RecentServerIds: ["a", "b", "c", "d"]).Normalize();
        preferences.FavoriteServerIds.Should().Equal("de", "fi");
        preferences.RecentServerIds.Should().Equal("a", "b", "c");
        new ColituVpnPreferences().Normalize().FavoriteServerIds.Should().BeEmpty();
    }
}
