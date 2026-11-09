# BetterMusic

BetterMusic is a native Windows music player derived from [WinYandex](https://github.com/pixiemays/WinYandex), upstream commit `c532409cab0e3232ebd48010fa4d36d5489074f7`. The original project and its contributors retain their copyright; this derivative is distributed under GPL-3.0. See [LICENSE](LICENSE) and [the upstream README](README_UPSTREAM.md).

This initial import retains WinYandex's C#, WinUI 3 and .NET 8 implementation. BetterMusic changes credential persistence to Windows-protected storage, removes automatic clipboard polling at login, and replaces the release workflow with a Windows x64 portable build artifact. Existing plaintext session and `.env` files from older versions are not automatically removed; users should remove old tokens themselves and revoke them if exposed.

Builds are produced by the Windows build workflow under GitHub Actions. This is an early development version; inherited functionality has not yet been verified with a Yandex account.
