<div align="center">
  <img src="https://sbox.game/img/sbox-logo-square.svg" width="80px" alt="s&box logo">

  [Website] | [Getting Started] | [Forums] | [Documentation] | [Contributing]
</div>

[Website]: https://sbox.game/
[Getting Started]: https://sbox.game/dev/doc/about/getting-started/first-steps/
[Forums]: https://sbox.game/f/
[Documentation]: https://sbox.game/dev/doc/
[Contributing]: CONTRIBUTING.md

# s&box — Linux Native Fork

This is a fork of [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) focused on running s&box natively on Linux without Proton.

![s&box editor](https://files.facepunch.com/matt/1b2211b1/sbox-dev_FoZ5NNZQTi.jpg)

## How This Fork Works

The s&box engine was built for Windows and relies on a case-insensitive filesystem, Windows-native input handling, and native Win32 APIs throughout its C++ layer. Running it on Linux requires patches at multiple levels:

- **Managed C# patches** — changes to the engine's C# layer to handle Linux display servers (X11, XWayland, Wayland), input routing via SDL3, and case-insensitive path resolution through the virtual filesystem
- **Native patches (via [Anvil](https://github.com/joshuascript/anvil))** — C shims preloaded at launch via `LD_PRELOAD` that intercept filesystem calls and patch native engine crashes in `libengine2.so` and `librendersystemvulkan.so`

The managed changes live in this repository. The native patches are managed separately by the [Anvil Project](https://github.com/joshuascript/anvil).

## Anvil — Required

**[Anvil](https://github.com/joshuascript/anvil) is required to run this fork.** It provides the native patch layer that the engine cannot function without on Linux.

Anvil is installed and kept up to date automatically by the Linux bootstrap script.

## Getting Started on Linux

### Prerequisites

- [Git](https://git-scm.com/)
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download)
- `gcc` (for compiling native patches)
- `python3` (for crash analysis tools)

### Setup

```bash
# Clone the repo
git clone https://github.com/joshuascript/sbox-public.git
cd sbox-public

# Run the Linux bootstrap — installs Anvil and builds managed artifacts
bash bootstrap
```

The bootstrap will automatically clone Anvil, compile the native patches, and walk you through building the managed engine artifacts.

### Launching

Always use the Anvil launch scripts — do not run the `sbox` binary directly.

```bash
# Normal launch
bash anvil/launch/launch-sbox.sh

# Launch with automated crash capture (GDB)
bash anvil/launch/launch-sbox-gdb.sh
```

Crash traces are written to `logs/gdb/` as numbered files per session.

## Contributing

If you would like to contribute to the engine, please see the [contributing guide](CONTRIBUTING.md).

If you want to report bugs or request new features, see [sbox-issues](https://github.com/Facepunch/sbox-public/issues/).

## Documentation

Full documentation, tutorials, and API references are available at [sbox.game/dev/](https://sbox.game/dev/).

## License

The s&box engine source code is licensed under the [MIT License](LICENSE.md).

Certain native binaries in `game/bin` are not covered by the MIT license. These binaries are distributed under the s&box EULA. You must agree to the terms of the EULA to use them.

This project includes third-party components that are separately licensed.
Those components are not covered by the MIT license above and remain subject
to their original licenses as indicated in `game/thirdpartylegalnotices`.