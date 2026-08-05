# sensvault

easy place to store sensitivities across a library of games

sort by name, game, sens, dpi, cm/360
sensitivities are stored in `%APPDATA%\SensVault\data.json`

# download

grab either one from [releases](https://github.com/as9pa/sensvault/releases). windows x64.

| | size | needs .NET? |
|---|---|---|
| `SensVault-...-standalone.exe` | ~62 mb | **no** — the runtime is inside the exe, just run it |
| `SensVault-...-needs-dotnet10.zip` | ~90 kb | **yes** — [.NET 10 desktop runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

take the standalone unless you already have .NET 10 installed

# building it yourself

requires the .NET 10 SDK

```
dotnet run
```
