using EvMigration.Core;

namespace EvMigration.Tests;

public sealed class ProjectStructureTests
{
    [Fact]
    public void CoreAssembly_CanBeLoaded()
    {
        var assemblyName = typeof(CoreAssemblyMarker).Assembly.GetName().Name;

        Assert.Equal("EvMigration.Core", assemblyName);
    }
}
