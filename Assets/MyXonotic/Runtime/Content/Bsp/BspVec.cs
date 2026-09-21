namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Minimal 3-component float vector kept independent of UnityEngine so
    /// the parser assembly is testable with a plain .NET/Mono toolchain
    /// (no Unity runtime required). Editor glue converts these to
    /// UnityEngine.Vector3 at the boundary.
    /// </summary>
    public struct BspVec3
    {
        public float X;
        public float Y;
        public float Z;

        public BspVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public struct BspVec2
    {
        public float X;
        public float Y;

        public BspVec2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public struct BspColor32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public BspColor32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }
}
