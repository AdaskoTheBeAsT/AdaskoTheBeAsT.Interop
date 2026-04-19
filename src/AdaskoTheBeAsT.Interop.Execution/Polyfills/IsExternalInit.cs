#if !NET5_0_OR_GREATER
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// Polyfill for C# 9 init-only setters on legacy .NET Framework TFMs
// (net462 / net47 / net471 / net472 / net48 / net481).
// See https://github.com/dotnet/roslyn/blob/main/docs/features/init.md
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
