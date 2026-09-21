using System;

namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Raised for any BSP input that is malformed, uses an unsupported IBSP
    /// version/lump layout, or fails a bounds/sanity check. The parser is
    /// expected to throw this instead of crashing or silently producing
    /// corrupt geometry on hostile input.
    /// </summary>
    public sealed class BspFormatException : Exception
    {
        public BspFormatException(string message) : base(message)
        {
        }
    }
}
