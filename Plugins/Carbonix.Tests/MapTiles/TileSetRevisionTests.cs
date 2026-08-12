using Carbonix.MapTiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// Covers supersession: several bakes of the same area are revisions of one
    /// dataset, the newest draws, and the older ones stay reachable for the
    /// "the new bake dropped something, what did the old one say" case.
    /// </summary>
    [TestClass]
    public class TileSetRevisionTests
    {
        string _folder;
        TileSetCatalog _catalog;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CbxRev_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
        }

        [TestCleanup]
        public void Teardown()
        {
            _catalog?.Dispose();
            try
            {
                if (Directory.Exists(_folder))
                {
                    Directory.Delete(_folder, true);
                }
            }
            catch (IOException)
            {
            }
        }

        TileSetBuilder Bake(string file)
        {
            return new TileSetBuilder(_folder, file).Tile(4, 9, 6, "ink");
        }

        TileSetCatalog Load()
        {
            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();
            return _catalog;
        }

        TileSetGroup OnlyGroup()
        {
            var groups = _catalog.Groups();
            Assert.AreEqual(1, groups.Count, "expected the revisions to collapse into one dataset");
            return groups[0];
        }

        [TestMethod]
        public void RevisionsOfOneReferenceCollapseIntoOneDataset()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            Load();

            var group = OnlyGroup();
            Assert.AreEqual(2, group.Revisions.Count);
            Assert.AreEqual("r2.0", group.Latest.Revision);
        }

        [TestMethod]
        public void OnlyTheNewestRevisionDraws()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            var catalog = Load();

            var active = catalog.ActiveSources();
            Assert.AreEqual(1, active.Count, "a superseded revision must not draw alongside its replacement");
            Assert.AreEqual("r2.0", active[0].Revision);
        }

        [TestMethod]
        public void GenerationTimeOrdersRevisionsNotTheLabel()
        {
            // The whole reason ordering is on the timestamp: a manual bake can
            // forget to bump the label, and that must stay a cosmetic bug.
            Bake("a_first.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_second.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-03-05T09:00:00Z").Build();

            var catalog = Load();

            Assert.AreEqual(1, catalog.ActiveSources().Count);
            Assert.AreEqual("a_second", OnlyGroup().Latest.Name);
        }

        [TestMethod]
        public void UndatedRevisionNeverSupersedesADatedOne()
        {
            Bake("a_dated.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_undated.mbtiles").Reference("BVLOS-0082").Revision("r9.9").Build();

            Load();

            Assert.AreEqual("r2.0", OnlyGroup().Latest.Revision,
                "a bake with no generation time sorts oldest, whatever it calls itself");
        }

        [TestMethod]
        public void FileWithoutAReferenceIsItsOwnDataset()
        {
            Bake("loose_one.mbtiles").NoReference().Build();
            Bake("loose_two.mbtiles").NoReference().Build();

            var catalog = Load();

            Assert.AreEqual(2, catalog.Groups().Count, "unmanaged files must not be lumped together");
            Assert.IsTrue(catalog.Groups().All(g => !g.HasDeclaredDataset));
            Assert.AreEqual(2, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void PeekShowsAnOlderRevisionInsteadOfTheNewest()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            var catalog = Load();
            var group = OnlyGroup();
            var old = group.Revisions.Single(r => r.Revision == "r1.0");

            catalog.Peek(group.Key, old);

            var after = OnlyGroup();
            Assert.IsTrue(after.IsPeeking);
            Assert.AreEqual("r1.0", after.Selected.Revision);
            Assert.AreEqual("r1.0", catalog.ActiveSources().Single().Revision);
        }

        [TestMethod]
        public void PeekingIsNotPersisted()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            var catalog = Load();
            var group = OnlyGroup();
            catalog.Peek(group.Key, group.Revisions.Single(r => r.Revision == "r1.0"));

            // Whatever gets written to settings must not carry the peek. Only
            // the disabled set and the out-of-folder paths persist.
            CollectionAssert.DoesNotContain(catalog.DisabledKeys.ToList(), group.Key);
            Assert.AreEqual(0, catalog.ExtraPaths.Count());
        }

        [TestMethod]
        public void PeekClearsBackToLatest()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            var catalog = Load();
            var group = OnlyGroup();
            catalog.Peek(group.Key, group.Revisions.Single(r => r.Revision == "r1.0"));
            catalog.Peek(group.Key, null);

            Assert.IsFalse(OnlyGroup().IsPeeking);
            Assert.AreEqual("r2.0", catalog.ActiveSources().Single().Revision);
        }

        [TestMethod]
        public void DisablingIsKeyedOnTheDatasetSoANewRevisionStaysOff()
        {
            // The hazard this guards: drop a corrected bake into the folder and
            // a corridor the operator had deliberately switched off comes back,
            // because the new file's id was never in the disabled set.
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();

            var catalog = Load();
            catalog.SetEnabled(OnlyGroup().Key, false);
            Assert.AreEqual(0, catalog.ActiveSources().Count);

            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();
            catalog.Rescan();

            Assert.IsFalse(OnlyGroup().Enabled, "a new revision must inherit the dataset's state");
            Assert.AreEqual(0, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void AnExpiredLatestDoesNotFallBackToAnOlderRevision()
        {
            // Revision is chosen first, expiry checked second. "Newest
            // unexpired" would quietly resurrect last year's approval the moment
            // this year's lapsed, which is what expiry exists to prevent.
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2099-01-01").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();

            Assert.AreEqual("r2.0", OnlyGroup().Selected.Revision);
            Assert.IsTrue(OnlyGroup().Selected.IsExpired);
            Assert.AreEqual(0, catalog.ActiveSources().Count, "nothing should draw for a lapsed dataset");
        }

        [TestMethod]
        public void RemovingOneRevisionKeepsTheDatasetDisabled()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Build();
            Bake("a_r2.mbtiles").Reference("BVLOS-0082").Revision("r2.0")
                .Generated("2026-02-05T09:00:00Z").Build();

            var catalog = Load();
            var group = OnlyGroup();
            catalog.SetEnabled(group.Key, false);

            catalog.Remove(group.Revisions.Single(r => r.Revision == "r1.0"), deleteFile: false);

            Assert.IsFalse(OnlyGroup().Enabled,
                "tidying away a superseded file must not switch the corridor back on");
        }

        [TestMethod]
        public void RevisionLabelIsOptional()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082")
                .Generated("2026-01-05T09:00:00Z").Build();

            Load();

            Assert.IsNull(OnlyGroup().Latest.Revision);
            Assert.IsTrue(OnlyGroup().HasDeclaredDataset);
        }

        [TestMethod]
        public void ExpiredDatasetIsNotDrawnByDefault()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            var group = OnlyGroup();

            Assert.IsTrue(group.Enabled, "the operator never switched it off");
            Assert.IsTrue(group.IsExpired);
            Assert.IsFalse(group.IsDrawn, "expired means not drawn until overridden");
            Assert.AreEqual(0, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void ExpiryCanBeOverriddenDeliberately()
        {
            // The escape hatch: an approval that lapsed the morning of a
            // deployment still has to be viewable.
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            catalog.ForceExpired(OnlyGroup().Key, true);

            var group = OnlyGroup();
            Assert.IsTrue(group.ForcedExpired);
            Assert.IsTrue(group.IsExpired, "it is still expired; it is just being shown");
            Assert.IsTrue(group.IsDrawn);
            Assert.AreEqual(1, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void OverrideCanBeTakenBack()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            catalog.ForceExpired(OnlyGroup().Key, true);
            catalog.ForceExpired(OnlyGroup().Key, false);

            Assert.IsFalse(OnlyGroup().IsDrawn);
            Assert.AreEqual(0, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void OverridingExpiryIsNotPersisted()
        {
            // Same reasoning as peeking, and more so: a considered override must
            // not decay into a setting nobody remembers making.
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            catalog.ForceExpired(OnlyGroup().Key, true);

            CollectionAssert.DoesNotContain(catalog.DisabledKeys.ToList(), OnlyGroup().Key);
            Assert.AreEqual(0, catalog.ExtraPaths.Count(),
                "nothing about the override may reach the persisted settings");
        }

        [TestMethod]
        public void OverrideDoesNotLeakToOtherDatasets()
        {
            Bake("a_r1.mbtiles").Reference("AAA-1").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();
            Bake("b_r1.mbtiles").Reference("BBB-2").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            catalog.ForceExpired("AAA-1", true);

            var drawn = catalog.ActiveSources();
            Assert.AreEqual(1, drawn.Count);
            Assert.AreEqual("a_r1", drawn[0].Name);
        }

        [TestMethod]
        public void OverridingAnExpiredDatasetThatIsSwitchedOffStillDrawsNothing()
        {
            // Two independent reasons not to draw. Clearing one must not clear
            // the other.
            Bake("a_r1.mbtiles").Reference("BVLOS-0082").Revision("r1.0")
                .Generated("2026-01-05T09:00:00Z").Expires("2020-01-01").Build();

            var catalog = Load();
            catalog.SetEnabled(OnlyGroup().Key, false);
            catalog.ForceExpired(OnlyGroup().Key, true);

            Assert.IsFalse(OnlyGroup().IsDrawn);
            Assert.AreEqual(0, catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void GeneratedIsReadAsUtc()
        {
            Bake("a_r1.mbtiles").Reference("BVLOS-0082")
                .Generated("2026-01-05T09:00:00Z").Build();

            Load();

            var generated = OnlyGroup().Latest.Generated;
            Assert.IsTrue(generated.HasValue);
            Assert.AreEqual(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), generated.Value.ToUniversalTime());
        }
    }
}
