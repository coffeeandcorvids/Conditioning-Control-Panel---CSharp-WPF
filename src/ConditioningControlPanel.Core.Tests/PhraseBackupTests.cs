using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Settings;
using Xunit;

public class PhraseBackupTests
{
    static PanelSettings WithPhrases() => new()
    {
        SubliminalPhrases  = new List<string> { "good girl", "obey", "deeper" },
        LockCardPhrases    = new List<string> { "good girls don't think" },
        BouncingTextPhrases = new List<string> { "sink", "drift" },
        Keywords           = new List<string> { "spiral" },
    };

    static string TempPath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ccpphrases.json");

    [Fact]
    public void Export_then_import_round_trips_every_pool()
    {
        var svc = new PhraseBackupService();
        var path = TempPath();
        try
        {
            var written = svc.Export(WithPhrases(), path);
            Assert.Equal(7, written);

            var restored = new PanelSettings
            {
                SubliminalPhrases = new List<string> { "stale" },
                LockCardPhrases = new List<string>(),
            };
            var applied = svc.Import(restored, path);

            Assert.Equal(7, applied);
            Assert.Equal(new[] { "good girl", "obey", "deeper" }, restored.SubliminalPhrases);
            Assert.Equal(new[] { "good girls don't think" }, restored.LockCardPhrases);
            Assert.Equal(new[] { "sink", "drift" }, restored.BouncingTextPhrases);
            Assert.Equal(new[] { "spiral" }, restored.Keywords);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Import_is_a_restore_not_a_merge()
    {
        var svc = new PhraseBackupService();
        var path = TempPath();
        try
        {
            svc.Export(WithPhrases(), path);
            var target = WithPhrases();
            target.SubliminalPhrases.Add("later addition");
            svc.Import(target, path);
            Assert.DoesNotContain("later addition", target.SubliminalPhrases);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Import_only_touches_whitelisted_phrase_pools()
    {
        // a crafted backup must not be able to set arbitrary settings
        var svc = new PhraseBackupService();
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """
            {
              "schema": "ccp-phrases/v1",
              "phrases": {
                "SubliminalPhrases": ["injected ok"],
                "AssetsRoot": ["/evil/path"],
                "LettaAgentId": ["agent-evil"]
              }
            }
            """);
            var settings = new PanelSettings();
            var before = settings.AssetsRoot;
            svc.Import(settings, path);
            Assert.Equal(new[] { "injected ok" }, settings.SubliminalPhrases);
            Assert.Equal(before, settings.AssetsRoot);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Validate_rejects_wrong_schema_and_empty_files()
    {
        var svc = new PhraseBackupService();
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{ "schema": "other/v9", "phrases": { "x": ["y"] } }""");
            Assert.False(svc.Validate(path, out var err1));
            Assert.Contains("schema", err1);

            File.WriteAllText(path, """{ "schema": "ccp-phrases/v1", "phrases": {} }""");
            Assert.False(svc.Validate(path, out var err2));
            Assert.Contains("No phrases", err2);

            new PhraseBackupService().Export(WithPhrases(), path);
            Assert.True(svc.Validate(path, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Import_skips_malformed_members_without_failing()
    {
        var svc = new PhraseBackupService();
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """
            {
              "schema": "ccp-phrases/v1",
              "phrases": {
                "SubliminalPhrases": "not-an-array",
                "LockCardPhrases": ["still lands"]
              }
            }
            """);
            var settings = new PanelSettings();
            var applied = svc.Import(settings, path);
            Assert.Equal(1, applied);
            Assert.Equal(new[] { "still lands" }, settings.LockCardPhrases);
        }
        finally { File.Delete(path); }
    }
}
