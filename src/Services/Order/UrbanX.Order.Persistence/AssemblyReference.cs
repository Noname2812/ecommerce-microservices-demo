using System.Reflection;

namespace UrbanX.Order.Persistence;

internal static class AssemblyReference
{
    internal static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
