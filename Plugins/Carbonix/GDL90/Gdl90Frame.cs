using System;
using System.Collections.Generic;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Provides GDL 90 datalink framing per ICD 560-1058-00 Rev A section 2.2:
    /// flag-byte delimiting, byte-stuffing, and the CRC-CCITT frame check sequence.
    /// </summary>
    public static class Gdl90Frame
    {
        /// <summary>Delimits the start and end of a frame.</summary>
        public const byte FlagByte = 0x7E;

        /// <summary>Introduces a stuffed byte.</summary>
        public const byte ControlEscape = 0x7D;
        private const byte EscapeXor = 0x20;

        private static readonly ushort[] Crc16Table = BuildCrc16Table();

        private static ushort[] BuildCrc16Table()
        {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                int crc = i << 8;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)) & 0xFFFF;
                }
                table[i] = (ushort)crc;
            }
            return table;
        }

        /// <summary>Computes the frame check sequence over a range of bytes.</summary>
        /// <param name="data">Buffer holding the bytes to check.</param>
        /// <param name="offset">Index of the first byte to include.</param>
        /// <param name="length">Number of bytes to include.</param>
        /// <returns>The 16-bit frame check sequence.</returns>
        /// <remarks>
        /// CRC-CCITT, polynomial 0x1021 with an initial value of zero, per ICD section
        /// 2.2.3.
        /// </remarks>
        // The ICD's own routine, which folds the data byte into the result after the
        // table lookup rather than into the index. That is not the textbook table CRC,
        // so Mission Planner's sbp.Crc16Ccitt gives different values and cannot stand
        // in for this.
        public static ushort Crc16(byte[] data, int offset, int length)
        {
            int crc = 0;
            for (int i = 0; i < length; i++)
            {
                crc = (Crc16Table[crc >> 8] ^ (crc << 8) ^ data[offset + i]) & 0xFFFF;
            }
            return (ushort)crc;
        }

        /// <summary>Computes the frame check sequence over a whole buffer.</summary>
        /// <param name="data">The bytes to check.</param>
        /// <returns>The 16-bit frame check sequence.</returns>
        public static ushort Crc16(byte[] data)
        {
            return Crc16(data, 0, data.Length);
        }

        /// <summary>Wraps a clear message in a transmittable frame.</summary>
        /// <param name="message">
        /// The clear message: Message ID and data, without a frame check sequence.
        /// </param>
        /// <returns>The framed bytes, ready to transmit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
        /// <remarks>
        /// Appends the frame check sequence least significant byte first, byte-stuffs
        /// every <see cref="FlagByte"/> and <see cref="ControlEscape"/> between the
        /// flags, and adds a flag byte at each end.
        /// </remarks>
        public static byte[] Frame(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            ushort crc = Crc16(message);
            var framed = new List<byte>(message.Length + 6) { FlagByte };
            for (int i = 0; i < message.Length; i++)
            {
                AppendStuffed(framed, message[i]);
            }
            AppendStuffed(framed, (byte)(crc & 0xFF));
            AppendStuffed(framed, (byte)(crc >> 8));
            framed.Add(FlagByte);
            return framed.ToArray();
        }

        private static void AppendStuffed(List<byte> framed, byte b)
        {
            if (b == FlagByte || b == ControlEscape)
            {
                framed.Add(ControlEscape);
                framed.Add((byte)(b ^ EscapeXor));
            }
            else
            {
                framed.Add(b);
            }
        }

        /// <summary>Recovers the clear message from a frame.</summary>
        /// <param name="frame">One frame, including the flag byte at each end.</param>
        /// <param name="message">
        /// When this method returns, the clear message (Message ID and data, without
        /// the frame check sequence), or null if the frame could not be read.
        /// </param>
        /// <returns>
        /// true if the framing and byte-stuffing were well formed and the frame check
        /// sequence matched; otherwise, false.
        /// </returns>
        /// <remarks>Reverses <see cref="Frame"/>.</remarks>
        public static bool TryUnframe(byte[] frame, out byte[] message)
        {
            message = null;
            if (frame == null || frame.Length < 5) return false;
            if (frame[0] != FlagByte || frame[frame.Length - 1] != FlagByte) return false;

            var clear = new List<byte>(frame.Length - 2);
            for (int i = 1; i < frame.Length - 1; i++)
            {
                byte b = frame[i];
                if (b == FlagByte) return false;
                if (b == ControlEscape)
                {
                    if (++i >= frame.Length - 1) return false;
                    b = (byte)(frame[i] ^ EscapeXor);
                    if (b != FlagByte && b != ControlEscape) return false;
                }
                clear.Add(b);
            }

            // Message ID + at least the two FCS bytes.
            if (clear.Count < 3) return false;

            int fcsLo = clear[clear.Count - 2];
            int fcsHi = clear[clear.Count - 1];
            var body = clear.GetRange(0, clear.Count - 2).ToArray();
            if (Crc16(body) != (ushort)(fcsLo | (fcsHi << 8))) return false;

            message = body;
            return true;
        }
    }
}
