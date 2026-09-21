using System;

namespace MyXonotic.Content.Md3
{
    /// <summary>
    /// Raised for any MD3 input that is malformed, uses an unsupported
    /// ident/version, or fails a bounds/sanity check. Mirrors
    /// MyXonotic.Content.Bsp.BspFormatException's role for the BSP parser:
    /// the reader is expected to throw this instead of crashing or silently
    /// producing corrupt geometry on hostile/truncated input.
    /// </summary>
    public sealed class Md3FormatException : Exception
    {
        public Md3FormatException(string message) : base(message)
        {
        }
    }
}
