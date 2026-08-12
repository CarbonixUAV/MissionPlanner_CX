using Carbonix.MapTiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Carbonix.Tests.MapTiles
{
    [TestClass]
    public class TileSetCatalogTests
    {
        string _folder;
        string _outside;
        TileSetCatalog _catalog;

        static string Fixture(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        }

        [TestInitialize]
        public void Setup()
        {
            var root = Path.Combine(Path.GetTempPath(), "CarbonixTiles_" + Path.GetRandomFileName());
            _folder = Path.Combine(root, "mbtiles");
            _outside = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(_folder);
            Directory.CreateDirectory(_outside);
        }

        [TestCleanup]
        public void Teardown()
        {
            // Dispose first, or the copies stay locked and the delete fails
            _catalog?.Dispose();

            var root = Directory.GetParent(_folder).FullName;
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
                // a leaked handle would surface as the DisposeReleasesTheFile
                // failure instead; don't mask other results with a teardown throw
            }
        }

        string PlaceInFolder(string fixture)
        {
            var dest = Path.Combine(_folder, fixture);
            File.Copy(Fixture(fixture), dest, true);
            return dest;
        }

        string PlaceOutside(string fixture)
        {
            var dest = Path.Combine(_outside, fixture);
            File.Copy(Fixture(fixture), dest, true);
            return dest;
        }

        [TestMethod]
        public void Rescan_FindsTilesetsInTheFolder()
        {
            PlaceInFolder("sample.mbtiles");
            PlaceInFolder("expired.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            Assert.AreEqual(2, _catalog.Sources.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Test Corridor", "Old Approval" },
                _catalog.Sources.Select(s => s.Name).ToArray());
        }

        [TestMethod]
        public void Rescan_CreatesFolderAndCopesWithNothingThere()
        {
            var missing = Path.Combine(_folder, "nested", "deeper");

            _catalog = new TileSetCatalog(missing);
            _catalog.Rescan();

            Assert.AreEqual(0, _catalog.Sources.Count);
            Assert.IsTrue(Directory.Exists(missing));
        }

        [TestMethod]
        public void ActiveSources_SkipsExpired()
        {
            PlaceInFolder("sample.mbtiles");
            PlaceInFolder("expired.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            var active = _catalog.ActiveSources();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Test Corridor", active[0].Name);
        }

        [TestMethod]
        public void ActiveSources_SkipsDisabled()
        {
            PlaceInFolder("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            var source = _catalog.Sources.Single();
            Assert.IsTrue(_catalog.IsEnabled(source));

            _catalog.SetEnabled(source, false);

            Assert.IsFalse(_catalog.IsEnabled(source));
            Assert.AreEqual(0, _catalog.ActiveSources().Count);

            // Keyed on the dataset, so a later revision of the same reference
            // inherits the choice instead of silently coming back on.
            CollectionAssert.AreEqual(new[] { source.DatasetKey }, _catalog.DisabledKeys.ToArray());
        }

        [TestMethod]
        public void Restore_ReappliesDisabledStateAcrossReload()
        {
            PlaceInFolder("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Restore(null, new[] { "TEST-001" });
            _catalog.Rescan();

            Assert.AreEqual(1, _catalog.Sources.Count);
            Assert.AreEqual(0, _catalog.ActiveSources().Count);
        }

        [TestMethod]
        public void Add_RemembersFilesFromOutsideTheFolder()
        {
            var outside = PlaceOutside("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();
            Assert.AreEqual(0, _catalog.Sources.Count);

            _catalog.Add(outside);

            Assert.AreEqual(1, _catalog.Sources.Count);
            CollectionAssert.AreEqual(new[] { outside }, _catalog.ExtraPaths.ToArray());

            // and it survives a reload, which is the point of tracking it
            _catalog.Rescan();
            Assert.AreEqual(1, _catalog.Sources.Count);
        }

        [TestMethod]
        public void Add_DoesNotTrackFilesAlreadyInTheFolder()
        {
            var inside = PlaceInFolder("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Add(inside);

            Assert.AreEqual(1, _catalog.Sources.Count);
            Assert.AreEqual(0, _catalog.ExtraPaths.Count());
        }

        [TestMethod]
        public void Add_RefusesDuplicates()
        {
            PlaceInFolder("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            Assert.ThrowsException<InvalidOperationException>(
                () => _catalog.Add(Path.Combine(_folder, "sample.mbtiles")));
        }

        [TestMethod]
        public void Remove_CanDeleteTheFile()
        {
            var path = PlaceInFolder("expired.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            _catalog.Remove(_catalog.Sources.Single(), deleteFile: true);

            Assert.AreEqual(0, _catalog.Sources.Count);
            Assert.IsFalse(File.Exists(path), "clearing an expired tileset must actually delete it");
        }

        [TestMethod]
        public void Remove_CanLeaveTheFileAlone()
        {
            var outside = PlaceOutside("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Add(outside);
            _catalog.Remove(_catalog.Sources.Single(), deleteFile: false);

            Assert.AreEqual(0, _catalog.Sources.Count);
            Assert.IsTrue(File.Exists(outside));
            Assert.AreEqual(0, _catalog.ExtraPaths.Count(), "should stop being reloaded too");
        }

        [TestMethod]
        public void Rescan_ReportsFilesItCannotOpen()
        {
            File.WriteAllText(Path.Combine(_folder, "broken.mbtiles"), "not a database");

            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();

            Assert.AreEqual(0, _catalog.Sources.Count);
            Assert.AreEqual(1, _catalog.Errors.Count(), "the UI needs to be able to say why");
        }

        [TestMethod]
        public void Changed_FiresOnEveryMutation()
        {
            PlaceInFolder("sample.mbtiles");

            _catalog = new TileSetCatalog(_folder);
            int fired = 0;
            _catalog.Changed += () => fired++;

            _catalog.Rescan();
            Assert.AreEqual(1, fired);

            _catalog.SetEnabled(_catalog.Sources.Single(), false);
            Assert.AreEqual(2, fired);
        }
    }
}
