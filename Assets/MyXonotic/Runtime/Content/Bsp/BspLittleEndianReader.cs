using System;

namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Explicit little-endian primitive reader over a byte buffer, with hard
    /// bounds checks on every read. IBSP is defined as little-endian
    /// regardless of host architecture, so we never rely on
    /// BitConverter.IsLittleEndian.
    /// </summary>
    public struct BspLittleEndianReader
    {
        private readonly byte[] _data;

        public BspLittleEndianReader(byte[] data)
        {
            _data = data;
        }

        public int Length
        {
            get { return _data.Length; }
        }

        private void CheckRange(int offset, int size)
        {
            if (offset < 0 || size < 0)
            {
                throw new BspFormatException("Negative offset/size while reading BSP data.");
            }
            // Use long arithmetic to avoid signed-int overflow on hostile huge offsets.
            long end = (long)offset + size;
            if (end > _data.Length)
            {
                throw new BspFormatException(string.Format(
                    "BSP read out of bounds: offset={0} size={1} bufferLength={2}.",
                    offset, size, _data.Length));
            }
        }

        public int ReadInt32(int offset)
        {
            CheckRange(offset, 4);
            return _data[offset]
                 | (_data[offset + 1] << 8)
                 | (_data[offset + 2] << 16)
                 | (_data[offset + 3] << 24);
        }

        [ThreadStatic]
        private static byte[] _floatScratch;

        public float ReadFloat32(int offset)
        {
            CheckRange(offset, 4);
            // Copy the 4 little-endian bytes verbatim; every supported host
            // (x86/x64/ARM in Unity's runtimes) is little-endian, so
            // BitConverter.ToSingle interprets them identically without
            // needing Int32BitsToSingle (not available on older runtimes).
            if (_floatScratch == null) _floatScratch = new byte[4];
            _floatScratch[0] = _data[offset];
            _floatScratch[1] = _data[offset + 1];
            _floatScratch[2] = _data[offset + 2];
            _floatScratch[3] = _data[offset + 3];
            return BitConverter.ToSingle(_floatScratch, 0);
        }

        public byte ReadByte(int offset)
        {
            CheckRange(offset, 1);
            return _data[offset];
        }

        public string ReadFixedString(int offset, int maxLength)
        {
            CheckRange(offset, maxLength);
            int len = 0;
            while (len < maxLength && _data[offset + len] != 0) len++;
            return System.Text.Encoding.ASCII.GetString(_data, offset, len);
        }

        public string ReadAsciiRun(int offset, int length)
        {
            CheckRange(offset, length);
            return System.Text.Encoding.ASCII.GetString(_data, offset, length);
        }
    }
}
