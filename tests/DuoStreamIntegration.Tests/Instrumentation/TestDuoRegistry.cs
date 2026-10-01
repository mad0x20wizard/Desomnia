using Microsoft.Win32;

namespace DuoStreamIntegration.Tests;

// Redirect only this test's Duo registry reads; notifications still use real Windows keys.
internal sealed class TestDuoRegistry : IDisposable
{
    private static readonly AsyncLocal<TestDuoRegistry?> Scope = new();
    private readonly TestDuoRegistry? _previous;
    private readonly string _path = $@"Software\Desomnia.Tests\{Guid.NewGuid():N}";

    internal static TestDuoRegistry? Current => Scope.Value;
    internal RegistryKey Key { get; }

    internal TestDuoRegistry()
    {
        WindowsMocks.Initialize();
        Key = Registry.CurrentUser.CreateSubKey(_path);
        _previous = Scope.Value;
        Scope.Value = this;
    }

    public void Dispose()
    {
        Scope.Value = _previous;
        Key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path);
    }
}
