using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using v2rayN.Services;
using v2rayN.Views;
using Xunit;

namespace v2rayN.Tests;

public class ColituWhitelistInfoTests
{
    private static readonly string[] Keys = ["title", "lead", "when", "how", "note", "ok"];

    [Fact]
    public void ExplainerCopy_ExistsInEveryLanguage_AndNeverClaimsEncryption()
    {
        foreach (var key in Keys)
        {
            var values = Loc.ValuesOf("whitelist.info." + key);
            values.Should().HaveCount(Loc.Languages.Length);
            values.Should().OnlyContain(value => value.Length > 0);
            foreach (var value in values)
            {
                value.Should().NotContainEquivalentOf("encrypt").And.NotContainEquivalentOf("шифр").And.NotContain("加密");
            }
        }
    }

    [Fact]
    public void InfoButton_IsOnlyOnTheWhitelistRow()
    {
        var whitelist = new ColituVpnServer { Id = "w", Name = "w", WhitelistOk = true, Protocols = ["vless-ws"] };
        var plain = new ColituVpnServer { Id = "p", Name = "p", WhitelistOk = true, Protocols = ["vless-reality"] };

        ColituServerRow.Whitelist(whitelist).HasInfo.Should().BeTrue();
        ColituServerRow.From(whitelist).HasInfo.Should().BeFalse();
        ColituServerRow.From(plain).HasInfo.Should().BeFalse();
        ColituServerRow.Auto("x").HasInfo.Should().BeFalse();
    }

    /// <summary>Renders the explainer card to PNG when COLITU_RENDER_DIR is set (one file per language); otherwise only checks it builds.</summary>
    [Fact]
    public void ExplainerCard_RendersOffScreen()
    {
        var dir = Environment.GetEnvironmentVariable("COLITU_RENDER_DIR");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Render(dir);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure.Should().BeNull();
    }

    private static void Render(string? dir)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ColituVPN;component/Styles/ColituTheme.xaml", UriKind.Absolute)
        });

        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ColituVPN", "Views", "ColituMainWindow.xaml"));
        var text = File.ReadAllText(xamlPath);
        var start = text.IndexOf("<Grid x:Name=\"WhitelistInfo\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = text.IndexOf("<!-- ── This device is paused", start, StringComparison.Ordinal);
        var fragment = text[start..end].TrimEnd();

        foreach (var language in new[] { "tr", "en", "ru", "zh", "fa" })
        {
            Loc.I.SetLanguage(language);
            var xaml = Regex.Replace(fragment, @"\{s:T ([\w.]+)\}", match => System.Security.SecurityElement.Escape(Loc.I[match.Groups[1].Value]))
                .Replace("x:Name=\"WhitelistInfo\"", "x:Name=\"Overlay\"")
                .Replace("Visibility=\"Collapsed\"", "")
                .Replace("MouseLeftButtonDown=\"WhitelistInfoBackdrop_Click\"", "")
                .Replace("MouseLeftButtonDown=\"WhitelistInfoCard_Click\"", "")
                .Replace("Click=\"WhitelistInfoOk_Click\"", "")
                .Replace("x:Name=\"WhitelistInfoOk\"", "");
            xaml = Regex.Replace(xaml, "x:Name=\"WhitelistInfo(When|How|Note)\"", "Tag=\"$1\"");
            var root = (Grid)XamlReader.Parse(xaml.Replace("<Grid ", "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" ", StringComparison.Ordinal)
                .Replace("{StaticResource", "{StaticResource"));
            root.FlowDirection = Loc.I.FlowDirection;
            root.Width = 620;
            root.Height = 560;
            root.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0A, 0x14));
            foreach (var block in Descendants<TextBlock>(root).Where(block => block.Tag is "When" or "How" or "Note"))
            {
                ColituMainWindow.FillWhitelistInfo(block, Loc.I["whitelist.info." + block.Tag!.ToString()!.ToLowerInvariant()]);
            }
            root.Measure(new Size(620, 560));
            root.Arrange(new Rect(0, 0, 620, 560));
            root.UpdateLayout();

            if (dir == null) continue;
            var bitmap = new RenderTargetBitmap(620, 560, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(dir, $"whitelist-info-windows-{language}.png"));
            encoder.Save(stream);
        }
        Loc.I.SetLanguage("tr");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Fact]
    public void Search_FindsTheWhitelistRow_ByCountryCityAndWords()
    {
        var helsinki = new ColituVpnServer { Id = "fi", Name = "Finlandiya", DisplayName = "Finlandiya", CountryCode = "FI", City = "Helsinki", WhitelistOk = true, Protocols = ["vless-reality-relay"] };
        var row = ColituServerRow.Whitelist(helsinki);
        foreach (var q in new[] { "Helsinki", "хельсинки", "Finland", "Финляндия", "finlandiya", "beyaz liste", "Whitelist", "белый", "白名单", "لیست سفید" })
        {
            row.Matches(q).Should().BeTrue(q);
        }
        row.Matches("Prag").Should().BeFalse();
        ColituServerRow.From(helsinki).Matches("beyaz liste").Should().BeFalse("the normal row is not a whitelist row");
    }
}
