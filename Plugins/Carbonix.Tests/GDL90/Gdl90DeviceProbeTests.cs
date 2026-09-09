using System;
using System.Text;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.GDL90
{
    /// <summary>
    /// The reply parser, exercised against a real one.
    /// </summary>
    /// <remarks>
    /// A synthetic reply would only prove the parser agrees with whatever the test
    /// author believed the protocol was. This is what an iPad actually sent back when
    /// asked, captured on 2026-09-04: the answer's owner name is a compression pointer,
    /// the instance label carries a UTF-8 apostrophe, and five additional records follow
    /// that must be stepped over rather than tripped on.
    ///
    /// The device name, hostname, addresses and Rapport identifiers have been replaced
    /// with same-length stand-ins - the packet's structure is the point, and it is
    /// unchanged, down to the multi-byte character in the name.
    /// </remarks>
    [TestClass]
    public class Gdl90DeviceProbeTests
    {
        const ushort Id = 0x1234;

        const string RealReplyHex =
            "1234840000010001000000050f5f636f6d70616e696f6e2d6c696e6b045f746370056c6f63616c00000c0001c00c000c" +
            "00010000000a0013104f7073e28099206950616420466c6431c00cc038002100010000000a001600000000c0290d4f70" +
            "732d695061642d466c6431c021c038001000010000000a005b16727042413d30303a30303a30303a30303a30303a3030" +
            "11727041443d3636613238376366313034650c7270466c3d30783130303030117270484e3d3466636337383637623764" +
            "320772704d61633d300a727056723d3731352e32104f7073e28099206950616420466c64310c5f6465766963652d696e" +
            "666fc01c001000010000000a000d0c6d6f64656c3d4a3432304150c05d001c00010000000a0010fe8000000000000000" +
            "00000000000001c05d000100010000000a0004c0a80132";

        static byte[] RealReply()
        {
            return FromHex(RealReplyHex);
        }

        static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        [TestMethod]
        public void ReadReply_TakesTheDeviceNameOutOfARealDevicesAnswer()
        {
            var result = Gdl90DeviceProbe.ReadReply(RealReply(), Id);

            Assert.IsTrue(result.Answered);
            Assert.AreEqual("Ops\u2019 iPad Fld1", result.Name,
                "the name is UTF-8, and a real device put a curly apostrophe in it");
        }

        [TestMethod]
        public void ReadReply_TreatsAnythingThatRepliedAsAliveEvenWhenItCannotBeRead()
        {
            var truncated = new byte[20];
            Array.Copy(RealReply(), truncated, truncated.Length);

            var result = Gdl90DeviceProbe.ReadReply(truncated, Id);

            Assert.IsTrue(result.Answered);
            Assert.IsNull(result.Name);
        }

        /// <summary>
        /// The transaction id is the only thing tying a datagram to the question we
        /// asked. Without the check, a stale reply to a previous probe would answer for
        /// an address it was never about.
        /// </summary>
        [TestMethod]
        public void ReadReply_IgnoresAnAnswerToSomeOtherQuestion()
        {
            var result = Gdl90DeviceProbe.ReadReply(RealReply(), 0x9999);

            Assert.IsFalse(result.Answered);
            Assert.IsNull(result.Name);
        }

        [TestMethod]
        public void ReadReply_IgnoresAQuestionArrivingWhereAnAnswerWasExpected()
        {
            var query = Gdl90DeviceProbe.BuildQuery(Gdl90DeviceProbe.CompanionLink, Id);

            Assert.IsFalse(Gdl90DeviceProbe.ReadReply(query, Id).Answered,
                "the QR bit is clear, so this is a question");
        }

        [TestMethod]
        public void ReadReply_SurvivesRubbish()
        {
            Assert.IsFalse(Gdl90DeviceProbe.ReadReply(null, Id).Answered);
            Assert.IsFalse(Gdl90DeviceProbe.ReadReply(new byte[0], Id).Answered);
            Assert.IsFalse(Gdl90DeviceProbe.ReadReply(new byte[11], Id).Answered);
        }

        [TestMethod]
        public void BuildQuery_AsksOneServiceByNameInOneSmallDatagram()
        {
            var query = Gdl90DeviceProbe.BuildQuery(Gdl90DeviceProbe.CompanionLink, Id);

            Assert.AreEqual(44, query.Length);
            Assert.AreEqual(0x12, query[0]);
            Assert.AreEqual(0x34, query[1]);
            Assert.AreEqual(0, query[2], "a standard query, with no flags set");
            Assert.AreEqual(1, query[5], "exactly one question");
            Assert.AreEqual(0, query[7], "and no answers offered");

            var text = Encoding.ASCII.GetString(query);
            StringAssert.Contains(text, "_companion-link");
            StringAssert.Contains(text, "_tcp");
            StringAssert.Contains(text, "local");

            // PTR, class IN.
            Assert.AreEqual(12, query[query.Length - 3]);
            Assert.AreEqual(1, query[query.Length - 1]);
        }

        [TestMethod]
        public void BuildQuery_MatchesTheQuestionTheRealDeviceEchoedBack()
        {
            var query = Gdl90DeviceProbe.BuildQuery(Gdl90DeviceProbe.CompanionLink, Id);
            var reply = RealReply();

            for (int i = 12; i < query.Length; i++)
                Assert.AreEqual(query[i], reply[i], "question byte " + i);
        }
    }
}
