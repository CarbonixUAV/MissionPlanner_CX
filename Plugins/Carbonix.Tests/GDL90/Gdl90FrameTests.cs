using System;
using Carbonix.GDL90;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.GDL90
{
    [TestClass]
    public class Gdl90FrameTests
    {
        // ICD 2.2.4: the worked heartbeat example, flags and CRC included.
        private static readonly byte[] IcdHeartbeatFrame =
            Gdl90TestUtil.FromHex("7E008141DBD00802B38B7E");

        /// <summary>
        /// One assertion pins the flag bytes, the FCS and its least-significant-byte-
        /// first order together.
        /// </summary>
        [TestMethod]
        public void Frame_ReproducesIcdHeartbeatExample()
        {
            byte[] framed = Gdl90Frame.Frame(Gdl90TestUtil.FromHex("008141DBD00802"));

            CollectionAssert.AreEqual(IcdHeartbeatFrame, framed);
        }

        [TestMethod]
        public void Frame_StuffsFlagByteInMessageBody()
        {
            // ICD 2.2.1: "start of message ID #2 with second byte 0x7E: 0x7E 0x02 0x7D 0x5E ..."
            byte[] framed = Gdl90Frame.Frame(new byte[] { 0x02, 0x7E });
            Assert.AreEqual(0x7E, framed[0]);
            Assert.AreEqual(0x02, framed[1]);
            Assert.AreEqual(0x7D, framed[2]);
            Assert.AreEqual(0x5E, framed[3]);
        }

        [TestMethod]
        public void Frame_StuffsControlEscapeInMessageBody()
        {
            // ICD 2.2.1: "start of message ID #3 with second byte 0x7D: 0x7E 0x03 0x7D 0x5D ..."
            byte[] framed = Gdl90Frame.Frame(new byte[] { 0x03, 0x7D });
            Assert.AreEqual(0x7E, framed[0]);
            Assert.AreEqual(0x03, framed[1]);
            Assert.AreEqual(0x7D, framed[2]);
            Assert.AreEqual(0x5D, framed[3]);
        }

        /// <summary>
        /// The FCS is stuffed like any other byte, which the ICD's worked
        /// example cannot show because its FCS is 0xB3 0x8B. This message's FCS
        /// is 0x7D 0x7E: both bytes special, adjacent, and taking different
        /// escapes.
        /// </summary>
        [TestMethod]
        public void Frame_StuffsCrcBytes()
        {
            byte[] framed = Gdl90Frame.Frame(new byte[] { 0x14, 0x2C, 0xC8 });

            CollectionAssert.AreEqual(
                new byte[] { 0x7E, 0x14, 0x2C, 0xC8, 0x7D, 0x5D, 0x7D, 0x5E, 0x7E },
                framed);
        }

        [TestMethod]
        public void FrameThenUnframe_RoundTripsRandomMessages()
        {
            var rng = new Random(12345);
            for (int i = 0; i < 500; i++)
            {
                var message = new byte[1 + rng.Next(50)];
                rng.NextBytes(message);
                // Bias in framing-sensitive bytes so stuffing is exercised often.
                for (int j = 0; j < message.Length; j++)
                {
                    if (rng.Next(4) == 0) message[j] = rng.Next(2) == 0 ? (byte)0x7D : (byte)0x7E;
                }

                byte[] framed = Gdl90Frame.Frame(message);
                Assert.IsTrue(Gdl90Frame.TryUnframe(framed, out byte[] recovered));
                CollectionAssert.AreEqual(message, recovered);
            }
        }

        [TestMethod]
        public void TryUnframe_RejectsCorruptedCrc()
        {
            byte[] framed = (byte[])IcdHeartbeatFrame.Clone();
            framed[4] ^= 0x01; // flip a bit in the message body
            Assert.IsFalse(Gdl90Frame.TryUnframe(framed, out _));
        }

        [TestMethod]
        public void TryUnframe_RejectsMalformedFrames()
        {
            Assert.IsFalse(Gdl90Frame.TryUnframe(null, out _));
            Assert.IsFalse(Gdl90Frame.TryUnframe(new byte[0], out _));
            Assert.IsFalse(Gdl90Frame.TryUnframe(Gdl90TestUtil.FromHex("7E7E"), out _));
            // Missing trailing flag.
            Assert.IsFalse(Gdl90Frame.TryUnframe(Gdl90TestUtil.FromHex("7E008141DBD00802B38B"), out _));
            // Escape with nothing after it.
            Assert.IsFalse(Gdl90Frame.TryUnframe(Gdl90TestUtil.FromHex("7E00817D7E"), out _));
            // Escape followed by a byte that is not an escaped 0x7D/0x7E.
            Assert.IsFalse(Gdl90Frame.TryUnframe(Gdl90TestUtil.FromHex("7E00817D00AAAA7E"), out _));
        }
    }
}
