using AwesomeAssertions;
using ServiceLib.Models;
using SQLite;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// sqlite-net 1.11 (SQLitePCLRaw 3, SourceGear.sqlite3) must open a profile database written by
/// the 1.9 line the app used before. TestData/legacy-guiNDB.db was written with sqlite-net 1.9.172
/// and has two ProfileItem columns removed (CertSha, EchConfigList), as an older app version left
/// it; ProfileGroupItem is missing. Opening it as the app does must keep every row and add the
/// missing columns and table.
/// </summary>
public class LegacyDatabaseTests
{
    private static string CopyFixture()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "legacy-guiNDB.db");
        var copy = Path.Combine(Path.GetTempPath(), $"colitu-legacy-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        return copy;
    }

    [Fact]
    public async Task OldDatabase_OpensMigratesAndKeepsTheRows()
    {
        var path = CopyFixture();
        try
        {
            using (var db = new SQLiteConnection(path, false))
            {
                db.CreateTable<SubItem>().Should().Be(CreateTableResult.Migrated, "the table exists");
                db.CreateTable<ProfileItem>().Should().Be(CreateTableResult.Migrated);
                db.CreateTable<ServerStatItem>();
                db.CreateTable<RoutingItem>();
                db.CreateTable<ProfileExItem>();
                db.CreateTable<DNSItem>();
                db.CreateTable<FullConfigTemplateItem>();
#pragma warning disable CS0618
                db.CreateTable<ProfileGroupItem>().Should().Be(CreateTableResult.Created);
#pragma warning restore CS0618

                db.GetTableInfo(nameof(ProfileItem)).Select(column => column.Name).Should().Contain(["CertSha", "EchConfigList"]);

                var profiles = db.Table<ProfileItem>().OrderBy(item => item.IndexId).ToList();
                profiles.Select(item => item.IndexId).Should().Equal("legacy-hy2", "legacy-vless");
                var vless = profiles.Single(item => item.IndexId == "legacy-vless");
                vless.Address.Should().Be("node.example.test");
                vless.Port.Should().Be(443);
                vless.StreamSecurity.Should().Be("reality");
                vless.PublicKey.Should().Be("PBK");
                vless.Flow.Should().Be("xtls-rprx-vision");
                vless.CertSha.Should().BeNullOrEmpty("the new column starts empty");
                profiles.Single(item => item.IndexId == "legacy-hy2").ConfigType.Should().Be(ServiceLib.Enums.EConfigType.Hysteria2);

                db.Table<SubItem>().Single().Id.Should().Be("colitu-api");
                db.Table<RoutingItem>().Single().Id.Should().Be("colitu-routing");

                // Written back by the new library and read again.
                vless.CertSha = "abc";
                db.Update(vless).Should().Be(1);
            }

            var async = new SQLiteAsyncConnection(path, false);
            (await async.Table<ProfileItem>().Where(item => item.IndexId == "legacy-vless").FirstAsync()).CertSha.Should().Be("abc");
            await async.CloseAsync();
        }
        finally
        {
            SQLiteAsyncConnection.ResetPool();
            File.Delete(path);
        }
    }

    [Fact]
    public void NativeSqlite_IsCurrent()
    {
        using var db = new SQLiteConnection(":memory:", false);
        var version = db.ExecuteScalar<string>("select sqlite_version()");
        // CVE-2025-6965 is fixed in SQLite 3.50.2.
        Version.Parse(version).Should().BeGreaterThanOrEqualTo(new Version(3, 50, 2));
    }
}
