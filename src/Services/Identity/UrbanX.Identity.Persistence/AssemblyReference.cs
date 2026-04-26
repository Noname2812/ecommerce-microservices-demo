using System.Reflection;

namespace UrbanX.Identity.Persistence;

internal static class AssemblyReference
{
    internal static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
