<p align="center">
  <img src="assets/brand/zfc-logo-256.png" width="112" alt="Zero File Commander logo">
</p>

<h1 align="center">Zero File Commander</h1>

<p align="center">
  The power of a classic dual-pane commander, with a modern Windows 11 feel.<br>
  <sub>Klasik çift panelli dosya yöneticilerinin gücü, modern Windows 11 tadında.</sub>
</p>

<p align="center">
  <a href="https://github.com/aliapaydin/zero-file-commander/actions/workflows/build.yml"><img src="https://github.com/aliapaydin/zero-file-commander/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-1f6fd6" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-1f6fd6" alt="Windows 10 | 11">
</p>

---

> **Status:** early development (v0.1). The app runs and browses folders; most of the features below are on the [roadmap](docs/ROADMAP.md).

## Highlights (planned)

- **Dual pane, keyboard first:** F3–F8, Tab to switch panes, marking, and a command palette (Ctrl+K)
- **Multi-monitor transfers:** drag between windows on different screens, plus an edge shelf and a "send to other window" shortcut
- **Transfer queue:** pause/resume, speed and ETA, and conflict handling that never blocks the queue
- **Previews everywhere:** a preview pane, a hover quick look with pinning, a video scene strip, and inline editing for text and code
- **Views:** details, list, icons and gallery thumbnails, sizing with Ctrl + mouse wheel, light and dark themes

## Build

Requirements: Windows 10 19041+ / Windows 11, Visual Studio 2026 (or 2022) with the **WinUI application development** workload, and the .NET 10 SDK.

```powershell
git clone https://github.com/aliapaydin/zero-file-commander
cd zero-file-commander
start ZeroFileCommander.sln   # set Platform = x64, press F5
```

## Repository layout

```
assets/brand/          Logo (SVG/PNG)
docs/                  Roadmap, design notes, publishing checklist
src/ZeroFileCommander/ WinUI 3 app (Windows App SDK, .NET 10, MSIX)
.github/workflows/     CI build (x64, ARM64)
```

## Türkçe özet

Zero File Commander, Total Commander'dan esinlenen modern bir Windows dosya yöneticisi. Hedefler şunlar:

- çift panel ve klavye öncelikli kullanım
- monitörler arası sürükle-bırak ve transfer kuyruğu
- her yerde hızlı önizleme
- galeri görünümü, aydınlık ve karanlık tema

Detaylar için [docs/ROADMAP.md](docs/ROADMAP.md) ve [docs/DESIGN.md](docs/DESIGN.md) dosyalarına bak.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).
*Katkılar açık; İngilizce ya da Türkçe yazabilirsin.*

## License

[MIT](LICENSE) © 2026 Ali Apaydın
