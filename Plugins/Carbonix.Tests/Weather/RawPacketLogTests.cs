using Carbonix.Weather;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Carbonix.Tests.Weather
{
    [TestClass]
    public class RawPacketLogTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 22, 3, 12, 0, 500, DateTimeKind.Utc);

        string _folder;

        [TestInitialize]
        public void MakeFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "cbx-rawlog-" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void RemoveFolder()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        [TestMethod]
        public void NothingUntilTheFirstPacket()
        {
            using (var log = new RawPacketLog(_folder))
            {
                Assert.IsNull(log.Path);
                Assert.IsFalse(Directory.Exists(_folder));
            }
            Assert.IsFalse(Directory.Exists(_folder));
        }

        [TestMethod]
        public void OneLinePerPacketWithReceiveTime()
        {
            string path;
            using (var log = new RawPacketLog(_folder))
            {
                log.Write(T0, "{\"type\":\"rapid_wind\"}");
                log.Write(T0.AddSeconds(3), "{\"type\":\"hub_status\"}");
                path = log.Path;
                Assert.AreEqual(Path.Combine(_folder, "tempest-20260922-031200.log"), path);
            }

            CollectionAssert.AreEqual(new[]
            {
                "2026-09-22T03:12:00.500Z\t{\"type\":\"rapid_wind\"}",
                "2026-09-22T03:12:03.500Z\t{\"type\":\"hub_status\"}",
            }, File.ReadAllLines(path));
            // No BOM ahead of the first timestamp
            Assert.AreEqual((byte)'2', File.ReadAllBytes(path)[0]);
        }

        [TestMethod]
        public void LinesAreReadableWhileOpen()
        {
            using (var log = new RawPacketLog(_folder))
            {
                log.Write(T0, "{\"a\":1}");
                using (var reader = new StreamReader(new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                    Assert.AreEqual("2026-09-22T03:12:00.500Z\t{\"a\":1}", reader.ReadLine());
            }
        }

        [TestMethod]
        public void WriteAfterDisposeOpensNothing()
        {
            var log = new RawPacketLog(_folder);
            log.Dispose();
            log.Write(T0, "{}");
            Assert.IsNull(log.Path);
            Assert.IsFalse(Directory.Exists(_folder));
        }

        [TestMethod]
        public void SecondLogInTheSameSecondGetsItsOwnFile()
        {
            using (var first = new RawPacketLog(_folder))
            using (var second = new RawPacketLog(_folder))
            {
                first.Write(T0, "{}");
                second.Write(T0, "{}");
                Assert.AreNotEqual(first.Path, second.Path);
                Assert.IsTrue(second.Path.EndsWith("tempest-20260922-031200-1.log"));
            }
        }
    }
}
