## Building from source / Developing

```powershell
dotnet build LuaToolsGui.sln
dotnet run --project src/LuaToolsGui/LuaToolsGui.csproj
```

For iterative development:

```powershell
dotnet watch --project src/LuaToolsGui/LuaToolsGui.csproj
```

### Test

```powershell
dotnet test
```

### Releases

Official builds are packaged and signed by the maintainers, so the release tooling isn't part of
this repo. Released builds are framework-dependent (~10 MB) and the setup auto-installs the .NET 10
Desktop Runtime on a clean machine; the app then self-updates through Velopack.

To produce a local build for testing, `dotnet publish -c Release` is enough.

### Layout

| Path | Contents |
|---|---|
| `src/LuaToolsGui/` | The application: `Views/` (XAML), `ViewModels/`, `Services/`, `Models/`, `Resources/` (localization) |
| `src/LuaToolsGui/AppConfig.cs` | All compiled-in endpoints, mirrors and public client values |
| `tests/LuaToolsGui.Tests/` | xUnit tests |
| `scripts/check-i18n.py` | Translation validator, run by CI on every RESX change |

## Contribution Guidelines

Two rules are non-negotiable.

### 1. Every user-facing string must be localized

The app ships 29 languages. **Never hardcode a visible string**, whether in XAML, a toast, a
`MessageBox`, or an empty-state label. Adding text means:

1. Add the key to the English source, `src/LuaToolsGui/Resources/Strings.resx`.
2. Add the accessor to the hand-maintained `Strings.Designer.cs`
   (`public static string Key => Get(nameof(Key));`).
3. Add the same key, with a real translation, to all `Strings.<tag>.resx` files. A key missing
   from any one of them is a bug.
4. Reference it. XAML: `Text="{x:Static res:Strings.Key}"`; C#: `Resources.Strings.Key` or
   `string.Format(Resources.Strings.Key, arg)`.

`.github/workflows/i18n-check.yml` runs `scripts/check-i18n.py` on every PR touching the RESX files.
Translation contributions are pull requests against those files. See
[`src/LuaToolsGui/Resources/README.md`](src/LuaToolsGui/Resources/README.md) for the translator
guide, including which terms to leave untranslated and why.

### 2. Every GitHub request goes through `GithubProxy`

The app must work in regions where `github.com` and `api.github.com` are blocked. **Never call
GitHub directly.** Route through `Services/GithubProxy.cs` (a DI singleton), which tries the direct
URL first and then falls through capability-matched mirrors:

```csharp
await gh.SendAsync(url, ct);                          // API / metadata
await gh.DownloadAsync(url, dest, progress, ct);      // release-asset binaries
```

The Velopack auto-updater is covered too. `UpdateService` passes a `ProxiedFileDownloader` that
reuses `GithubProxy.Candidates`, so the update feed and `.nupkg` fall back to mirrors as well.
