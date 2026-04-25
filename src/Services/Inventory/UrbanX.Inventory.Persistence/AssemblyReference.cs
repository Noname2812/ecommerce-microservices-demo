using System.Reflection;

namespace UrbanX.Inventory.Persistence;

internal static class AssemblyReference
{
    internal static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
