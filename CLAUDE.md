# Zero File Commander: çalışma notları

- WinUI 3 / Windows App SDK 2.x / .NET 10, tek proje MSIX: `src/ZeroFileCommander`.
- Hedef platformlar x64 ve ARM64. Derleme yalnızca Windows'ta yapılabiliyor; Linux ortamında doğrulama GitHub Actions (`.github/workflows/build.yml`) üzerinden.
- Arayüz metinleri Türkçe; ileride `.resw` ile yerelleştirilecek. Kod ve tanımlayıcılar İngilizce.
- Renkler için önce WinUI tema kaynaklarını kullan; marka renkleri `App.xaml` ve `docs/DESIGN.md` içinde.
- Dosya işlemleri şimdilik `Services/ShellFileOperations.cs` (SHFileOperation) üzerinden yapılıyor; kalıcı silme yok, her zaman Geri Dönüşüm Kutusu kullanılıyor.
- Yeni özellikleri `docs/ROADMAP.md` sırasıyla ekle ve tamamlananları işaretle.
