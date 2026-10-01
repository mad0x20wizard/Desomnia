namespace DuoStreamIntegration.Tests;

// Watcher selection reads the version through a plain ServiceController.
internal sealed class TestServiceVersion : IDisposable
{
    private static readonly AsyncLocal<TestServiceVersion?> Scope = new();
    private readonly TestServiceVersion? _previous;

    internal static TestServiceVersion? Current => Scope.Value;
    internal Version? Version { get; }

    internal TestServiceVersion(string? version)
    {
        WindowsMocks.Initialize();
        Version = version is null ? null : System.Version.Parse(version);
        _previous = Scope.Value;
        Scope.Value = this;
    }

    public void Dispose() => Scope.Value = _previous;
}
