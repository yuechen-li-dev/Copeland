//Apache2, 2014-2016, Samuel Carlsson, WinterDev

using System;
using System.IO;

namespace Typography.OpenFont
{
    sealed class ByteOrderSwappingBinaryReader : BinaryReader
    {
        private readonly byte[] _buffer = new byte[8];
        //All OpenType fonts use Motorola-style byte ordering (Big Endian)
        //
        public ByteOrderSwappingBinaryReader(Stream input)
            : base(input)
        {
        }

        public override short ReadInt16() => unchecked((short)ReadUInt16());

        public override ushort ReadUInt16()
        {
            ReadFieldExactly(2);
            return (ushort)((_buffer[0] << 8) | _buffer[1]);
        }

        public override uint ReadUInt32()
        {
            ReadFieldExactly(4);
            return ((uint)_buffer[0] << 24)
                | ((uint)_buffer[1] << 16)
                | ((uint)_buffer[2] << 8)
                | _buffer[3];
        }

        public override ulong ReadUInt64()
        {
            ReadFieldExactly(8);
            ulong value = 0;
            foreach (byte part in _buffer)
            {
                value = (value << 8) | part;
            }
            return value;
        }

        // CFF encodes these values as big-endian bytes too. Interpret their bits
        // explicitly rather than depending on the host's native byte order.
        public override double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64()));

        public override int ReadInt32() => unchecked((int)ReadUInt32());

        /// <summary>
        /// A Stream may return fewer bytes than requested without reaching EOF.
        /// Never interpret a partial field using bytes left by a previous read.
        /// </summary>
        private void ReadFieldExactly(int count)
        {
            int filled = 0;
            while (filled < count)
            {
                int read = base.Read(_buffer, filled, count - filled);
                if (read == 0)
                {
                    throw new EndOfStreamException("Incomplete big-endian OpenType field.");
                }
                filled += read;
            }
        }

        //we don't use these methods in our OpenFont, so => throw the exception
        public override int PeekChar() { throw new NotImplementedException(); }
        public override int Read() { throw new NotImplementedException(); }
        public override int Read(byte[] buffer, int index, int count) => base.Read(buffer, index, count);
        public override int Read(char[] buffer, int index, int count) { throw new NotImplementedException(); }
        public override bool ReadBoolean() { throw new NotImplementedException(); }
        public override char ReadChar() { throw new NotImplementedException(); }
        public override char[] ReadChars(int count) { throw new NotImplementedException(); }
        public override decimal ReadDecimal() { throw new NotImplementedException(); }

        public override long ReadInt64() { throw new NotImplementedException(); }
        public override sbyte ReadSByte() { throw new NotImplementedException(); }
        public override float ReadSingle() { throw new NotImplementedException(); }
        public override string ReadString() { throw new NotImplementedException(); }
        //

    }
}
