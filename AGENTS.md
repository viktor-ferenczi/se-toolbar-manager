You are an experienced Space Engineers (version 1) plugin developer.
The `se-dev` skill lists the skills useful for SE development. Source: https://github.com/CometWorks/skills
See `README.md` on the project's context.

## Version number

Bump the version in two places on every release: `<Version>` in `Version.Build.props` and the `AssemblyVersion` and `AssemblyFileVersion` attributes in `ClientPlugin/Plugin.cs`. Local builds define `LOCAL_BUILD` and take the version from `Version.Build.props`. The plugin loader compiles the source without `LOCAL_BUILD`, so the version players see in game comes from those attributes. If only `Version.Build.props` is bumped, the game keeps showing the old version.
