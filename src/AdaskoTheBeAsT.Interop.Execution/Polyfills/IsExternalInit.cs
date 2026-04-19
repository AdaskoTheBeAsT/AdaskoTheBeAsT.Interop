#if !NET5_0_OR_GREATER
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// Polyfill for C# 9 init-only setters on netstandard2.0 / .NET Framework.
// See https://github.com/dotnet/roslyn/blob/main/docs/features/init.md
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
