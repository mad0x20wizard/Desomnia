# Plugin dependencies

Each dynamically loaded Desomnia plugin sets `IsDesomniaPlugin` in its project:

```xml
<PropertyGroup>
  <IsDesomniaPlugin>true</IsDesomniaPlugin>
</PropertyGroup>
```

`Directory.Build.targets` then imports the shared rules in `../build/Desomnia.Plugin.targets`. Helper applications and libraries used privately by a plugin, such as DesomniaSessionMinion and DesomniaPipe, do not set this property.

Reference the host or modules needed for compilation using ordinary project references. Windows plugins can reference DesomniaService, which already references the core and all core modules:

```xml
<ProjectReference Include="..\..\DesomniaService\DesomniaService.csproj" />
```

The shared rules recognize DesomniaCore, DesomniaService, and `modules/*/*.csproj`. They prevent these projects and their runtime/native/build-transitive assets from being bundled with plugins, including references added transitively by the SDK. There is no need to repeat the core module list or `Private`/`ExcludeAssets` metadata in each plugin. Plugin-private project references retain their normal behavior.

`DesomniaHostPackage` in the shared targets lists packages that must use the host's assembly instance even when a plugin's own packages depend on them. Currently these are System.ServiceProcess.ServiceController and System.Diagnostics.EventLog (including its Messages DLL). Filtering uses the resolved package ID, so versions come from the dependency graph and need no duplicate declarations in plugin projects. Both copy-local processing and SDK dependency-manifest generation receive the filtered runtime assets.

Keep compile-time references available: this policy changes packaging, not which APIs plugins may use. It applies to ordinary framework-dependent plugin builds and publishes; it does not add assembly-loader overrides or post-publish file deletion. Republish into a clean deployment directory when migrating an existing installation because publishing can leave obsolete files behind.

Run the packaging regression check from the repository root on Windows:

```powershell
./tests/PluginPackaging.Tests/VerifyPluginPackaging.ps1
```

The check publishes every opted-in plugin into fresh directories for portable and win-x64 configurations, verifies that host files and dependency-manifest entries are absent, and checks that private dependencies remain. It also verifies that helper projects do not import the plugin policy. Use `-Configuration Release` to check release output. Run this check when changing the rules or upgrading the .NET SDK, whose dependency-resolution targets provide the extension points used here.
