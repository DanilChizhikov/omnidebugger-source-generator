# OmniDebugger Source Generator
![CI](https://github.com/DanilChizhikov/omnidebugger-source-generator/actions/workflows/ci.yml/badge.svg?branch=master)

The Roslyn source generator behind [OmniDebugger](https://github.com/DanilChizhikov/OmniDebugger), a runtime cheat
and debug panel for Unity. OmniDebugger ships it prebuilt as `Runtime/Analyzers/DTech.OmniDebugger.SourceGenerator.dll`;
this repository holds its source, its tests and the script that builds it.

You do not need anything from here to use OmniDebugger. Come here to read how commands are generated, report a bug
in the generator, or build a version of your own.

## Table of Contents
- [What It Does](#what-it-does)
- [Diagnostics](#diagnostics)
- [Compatibility](#compatibility)
- [Building](#building)
    - [Prerequisites](#prerequisites)
    - [Getting the Source](#getting-the-source)
    - [Testing](#testing)
    - [Building Into a Package](#building-into-a-package)
- [Versioning and Releases](#versioning-and-releases)
- [Repository Layout](#repository-layout)
- [License](#license)

## What It Does
While Unity compiles an assembly that references OmniDebugger, the generator reads every member marked with
`[DebugCommand]`, together with `[DebugTags]`, `[DebugIcon]`, `[DebugRange]` and `[DebugOptions]` on it or its
parameters:

```csharp
[DebugCommand("Economy/Coins", Name = "Add Coins")]
public void AddCoins(int amount = 100) => _wallet.Coins += amount;
```

For each assembly it writes one class, `DTech.OmniDebugger.Generated.OmniDebuggerCommands_<Assembly>`, that:

- builds a `DebugCommand` per member out of direct calls — `target.AddCoins((int)arguments[0])`,
  `() => target.TimeScale` — so nothing is read through reflection and the IL2CPP linker sees every command used;
- hands a binder per declaring type to `CommandBinders` from `[RuntimeInitializeOnLoadMethod]` (and
  `[InitializeOnLoadMethod]` in the editor), so `debugger.Commands.Register(obj)` finds the commands of `obj`'s type
  and of its base types;
- carries `[GeneratedCode("DTech.OmniDebugger.SourceGenerator", "<version>")]`, the version of the generator that
  wrote it.

It is an incremental generator (`IIncrementalGenerator`, `ForAttributeWithMetadataName`), so editing code that has no
commands does not rerun it.

## Diagnostics
A member that cannot be a command is a compile error naming the member and the reason, so a rejected command is never
silently missing:

| Id | Reason |
|---|---|
| `OMNI001` | The member is static — a command runs against the instance you register |
| `OMNI002` | The member is private or protected |
| `OMNI003` | The method returns a value — use a property to report one |
| `OMNI004` | The method is generic |
| `OMNI005` | A parameter is `ref`, `out`, `in`, a pointer or a ref struct |
| `OMNI006` | The property has no accessible getter |
| `OMNI007` | The property is an indexer |
| `OMNI008` | The declaring type, or one it is nested in, is private or protected |
| `OMNI009` | The declaring type is generic |
| `OMNI010` | The declaring type is not a class |
| `OMNI011` | `[DebugRange]` has bounds the wrong way round, or a negative step |
| `OMNI012` | The group path is blank |
| `OMNI013` | The name is blank or contains `/` |
| `OMNI014` | `[DebugOptions]` names a member the type and its base types do not have |
| `OMNI015` | The `[DebugOptions]` member is private or protected, an indexer, or a method that takes parameters |
| `OMNI016` | The `[DebugOptions]` member is not a collection (or a `Func` returning one), or yields a type the argument cannot take |

## Compatibility
The generator targets `netstandard2.0` and is compiled against `Microsoft.CodeAnalysis.CSharp` **4.3.1**, the compiler
Unity 6000.0 ships with. A newer Roslyn reference would build here and then fail to load in Unity — keep the pin.

It writes code against OmniDebugger's contracts, so each version goes with the OmniDebugger releases below:

| Generator | OmniDebugger |
|---|---|
| 2.2.0 | 2.2.x |
| 2.0.0 | 2.0.x – 2.1.x |

## Building

### Prerequisites
- .NET SDK 10 (the tests target `net10.0`; the generator itself is `netstandard2.0`).
- Git, to fetch the OmniDebugger submodule.

### Getting the Source
The tests compile the generator's output against OmniDebugger's real contracts, so OmniDebugger is a submodule in
`external/OmniDebugger`:

```sh
git clone --recursive https://github.com/DanilChizhikov/omnidebugger-source-generator.git
# or, in a clone made without --recursive:
git submodule update --init
```

The submodule is pinned to the OmniDebugger release the generator was last tested with. To test against another
checkout — a branch of OmniDebugger you are changing, say — point `OMNIDEBUGGER_PATH` at its package folder (the one
holding `package.json`); it takes precedence over the submodule.

### Testing
```sh
dotnet test
OMNIDEBUGGER_PATH=/path/to/OmniDebugger dotnet test
```

### Building Into a Package
```sh
./build.sh /path/to/OmniDebugger
```

`build.sh` runs the tests, builds the generator in Release and copies the DLL into `Runtime/Analyzers` of the package
it is given — as an argument or through `OMNIDEBUGGER_PATH`. Without a path it only builds and prints where the DLL
is. Unity keeps the DLL's import settings (the `RoslynAnalyzer` label, no platforms) in the `.meta` file already in the
package, so replacing the DLL is enough.

## Versioning and Releases
- Bump `<Version>` in `src/DTech.OmniDebugger.SourceGenerator/DTech.OmniDebugger.SourceGenerator.csproj` with every
  change to the generator. It is stamped into the DLL and into the `[GeneratedCode]` attribute of the generated code,
  which is how a project tells which generator it has.
- Note the change in [CHANGELOG.md](CHANGELOG.md).
- Tag the release `vX.Y.Z`: CI tests it, builds the DLL and attaches it to the GitHub release. Every push and pull
  request also uploads the DLL as a build artifact.

## Repository Layout
| Path | Contents |
|---|---|
| `src/DTech.OmniDebugger.SourceGenerator` | The generator |
| `tests/DTech.OmniDebugger.SourceGenerator.Tests` | xUnit tests: run the generator on a source and check its output and diagnostics |
| `external/OmniDebugger` | OmniDebugger, as a submodule — the contracts the tests compile against |
| `build.sh` | Test, build and copy into a package |

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
