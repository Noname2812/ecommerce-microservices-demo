using System.Reflection;

namespace UrbanX.Payment.Persistence;

internal static class AssemblyReference
{
    internal static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
