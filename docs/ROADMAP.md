# Yol haritası

Sürümler kabaca sıralı. Her madde mockup'taki bir ekrana karşılık geliyor (bkz. [DESIGN.md](DESIGN.md)).

## v0.1: İskelet ✅ (bu commit)
- [x] WinUI 3 + Windows App SDK + .NET 10, MSIX paketleme
- [x] Mica arka plan, özel başlık çubuğu, logo
- [x] İki panel: sürücü listesi, klasörde gezinme, `..` ile üst klasöre çıkma
- [x] Klavye: Enter, Backspace, Tab, F3, F4, F5, F6, F7, F8/Delete, Ctrl+R
- [x] Kopyala, taşı ve Geri Dönüşüm Kutusu'na silme Windows kabuğu üzerinden
- [x] GitHub Actions'ta x64 ve ARM64 derlemesi

## v0.2: Gerçek bir commander
- [ ] Sekmeler (Ctrl+T, Ctrl+W, sekmeyi diğer panele sürükleme)
- [ ] Sürücü çipleri ve doluluk göstergesi, tıklanabilir yol çubuğu (breadcrumb)
- [ ] İşaretleme: Insert/Space, renkli işaretli dosyalar, işaretli toplam boyut
- [ ] Sıralama (ad, uzantı, boyut, tarih) ve sütun genişliği
- [ ] Gerçek dosya simgeleri (shell icon), klasör izleme (FileSystemWatcher)
- [ ] Yeniden adlandırma (F2 / Shift+F6), satır içi düzenleme
- [ ] Ayarların kalıcı olması (son klasörler, panel başına görünüm)

## v0.3: Önizleme
- [ ] Önizleme paneli (F3 / Ctrl+Q): görsel, metin/kod, CSV, PDF, video
- [ ] Önizle ve düzenle ekranı: bölünmüş görünüm, sözdizimi renklendirme, kaydetme
- [ ] Hızlı bakış kartı: gecikme ayarı, Space ile tam ekran, sabitleme (P)
- [ ] Video sahne şeridi

## v0.4: Görünümler ve tema
- [ ] Detay, Liste, Simgeler ve Galeri modları, panel başına ayrı mod
- [ ] Ctrl + fare tekeri ile boyutlandırma (5 kademe)
- [ ] Küçük resim önbelleği (Windows thumbnail cache ve kendi disk önbelleğimiz)
- [ ] Aydınlık / karanlık / sistem teması

## v0.5: Transfer motoru
- [ ] Kendi transfer kuyruğumuz: duraklat/devam, hız, kalan süre, öncelik
- [ ] Bloklamayan çakışma çözümü ("yalnızca daha yeniyse değiştir" gibi kurallar)
- [ ] Klasör karşılaştırma ve senkronizasyon

## v0.6: Çoklu ekran
- [ ] Her monitörde ayrı pencere, pencereler arası sürükle-bırak
- [ ] Kenar rafı (Ctrl+Shift+S)
- [ ] Diğer pencereye gönder (Ctrl+Shift+→)

## v0.7: Komut paleti ve güç özellikleri
- [ ] Ctrl+K komut paleti: komutlar, klasörler, son dosyalar
- [ ] Toplu yeniden adlandırma
- [ ] ZIP/7z arşivlere klasör gibi girme
- [ ] Kabuk bağlam menüsü entegrasyonu

## v1.0: Yayın
- [ ] Erişilebilirlik ve performans turu
- [ ] Microsoft Store yayını (bkz. [PUBLISHING.md](PUBLISHING.md))
