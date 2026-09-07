#if !NET5_0_OR_GREATER
using System.ComponentModel;

// IDE0130 disabled: this polyfill MUST live in System.Runtime.CompilerServices for
// the C# compiler to recognize it as the init-only setter marker type.
// MA0182 disabled: the type is referenced by the compiler when emitting init setters
// on legacy .NET Framework TFMs, even though no source code references it directly.
#pragma warning disable IDE0130, MA0182
namespace System.Runtime.CompilerServices;

// Polyfill for C# 9 init-only setters on legacy .NET Framework TFMs
// (net472 / net48 / net481).
// See https://github.com/dotnet/roslyn/blob/main/docs/features/init.md
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit;
#pragma warning restore IDE0130, MA0182
#endif
