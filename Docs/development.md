# Development

This project is based on the
[Space Engineers client plugin template](https://github.com/viktor-ferenczi/se-client-plugin-template).

## Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/)
- [Python 3.12](https://python.org) (requires 3.12 or newer)
- [Pulsar](https://github.com/SpaceGT/Pulsar)
- [.NET Framework 4.8.1 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481) and
  [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Setting up a working copy

`Directory.Build.props` finds the game's `Bin64` folder through Steam. To override it, run
`setup.py`, which writes the detected path into `Directory.Build.props.user` (a local, **not
committed** file), or edit that file by hand.

Load the working copy through a Pulsar development folder: start Pulsar with `-sources`, then
add this repository with the Sources button. Building the solution does not deploy the plugin.
It is copied into Pulsar's `Local` plugin folder only if `Pulsar` is set in
`Directory.Build.props.user` or passed as `-p:Pulsar=...`.

## Plugin version

The plugin version lives in `Version.Build.props`, which **is** committed and imported by
`Directory.Build.props`. Bump the version there.

## Building and debugging on .NET 10

- Start the game with the `Interim.exe` Pulsar executable with the `-sources` command line option.
- Click on the Sources button in Pulsar's dialog, then register this repository as a development folder.
- Load `ToolbarManager.xml` (the PluginHub registration) as well.
- Select `Debug` mode and run `Interim.exe`, then attach the debugger.
- Select `Release` mode to test exactly how Pulsar will build and run the plugin on the player's machine.

## Accessing internal, protected and private members in game code

The [Krafs publicizer](https://github.com/krafs/Publicizer) is enabled for `Sandbox.Game` and
`Sandbox.Graphics`, see `ClientPlugin/Tools/GameAssembliesToPublicize.cs` and the `Publicize`
items in `ClientPlugin/ClientPlugin.csproj`.

## Checks

The in-game and offline checks are described in [Checks](checks.md).

## Release

- Always make the final release from a RELEASE build and test it before publishing.
- Pulsar compiles the source code on the player's machine, watch out for differences.

[Back to the README](../README.md)
