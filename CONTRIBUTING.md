# Contributing / Katkı

Thanks for your interest in Zero File Commander! Contributions in English or Turkish are welcome.
*Katkıların için teşekkürler! İngilizce ya da Türkçe yazabilirsin.*

## Before you start
- Check the [roadmap](docs/ROADMAP.md) and open [issues](https://github.com/aliapaydin/zero-file-commander/issues).
- For anything bigger than a small fix, open an issue first so we can agree on the approach.

## Development setup
1. Windows 10 19041+ or Windows 11, with **Developer Mode** turned on.
2. Visual Studio 2026 (or 2022) with the **WinUI application development** workload.
3. The .NET 10 SDK.
4. Open `ZeroFileCommander.sln`, choose the `x64` platform, and press F5.

## Pull requests
- Create a branch from `main` (`feature/tabs`, `fix/copy-conflict` …).
- Keep PRs focused, one feature or fix each.
- UI strings are Turkish for now; code, identifiers and comments in English are preferred.
- Use WinUI theme resources instead of hard-coded colors (see [docs/DESIGN.md](docs/DESIGN.md)).
- Deletes go to the Recycle Bin by default; permanent delete only via explicit Shift + confirmation.
- Make sure the CI build passes.

## License
By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
