# refs/ — portable reference assemblies (Mac / Linux builds)

The mod compiles against MelonLoader + the game's Il2Cpp interop assemblies.
On Windows the `.csproj` references them straight from the game install. On a
machine WITHOUT the game/MelonLoader installed (e.g. developing on a Mac), drop
copies of these 9 managed DLLs into this folder and the build will use them
automatically (the csproj switches to `refs/` when this folder exists).

These are **managed, platform-agnostic** assemblies, so DLLs copied from a
Windows or Linux install work fine for *compiling* on any OS (including Apple
Silicon) — you only need the game/MelonLoader to actually *run* the mod.

## What to copy here (flat, no subfolders)

From a machine that has the game + MelonLoader (after running the game once):

From `<game>/MelonLoader/net6/`:
- `MelonLoader.dll`
- `0Harmony.dll`
- `Il2CppInterop.Runtime.dll`
- `Il2CppInterop.Common.dll`
- `Newtonsoft.Json.dll`

From `<game>/MelonLoader/Il2CppAssemblies/`:
- `Il2Cppmscorlib.dll`
- `Il2CppSystem.dll`
- `UnityEngine.CoreModule.dll`
- `Assembly-CSharp.dll`

Then: `cd mod && dotnet build -c Debug` (needs the .NET 6 SDK).

> The `.dll` files here are git-ignored (they're game-derived); only this README
> is tracked. The Windows build ignores this folder entirely.
