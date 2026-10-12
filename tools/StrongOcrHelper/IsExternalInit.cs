namespace System.Runtime.CompilerServices
{
    // net48 has no IsExternalInit, but the third-party OCR options type exposes init-only setters,
    // which the compiler needs this marker type for. It carries no behaviour.
    internal static class IsExternalInit
    {
    }
}
