# Yol haritası

Sürümler kabaca sıralı. Her madde mockup'taki bir ekrana karşılık geliyor (bkz. [DESIGN.md](DESIGN.md)).

## v0.1: İskelet ✅
- [x] WinUI 3 + Windows App SDK + .NET 10, MSIX paketleme
- [x] Mica arka plan, özel başlık çubuğu, logo
- [x] İki panel: sürücü listesi, klasörde gezinme, `..` ile üst klasöre çıkma
- [x] Klavye: Enter, Backspace, Tab, F3, F4, F5, F6, F7, F8/Delete, Ctrl+R
- [x] Kopyala, taşı ve Geri Dönüşüm Kutusu'na silme Windows kabuğu üzerinden
- [x] GitHub Actions'ta x64 ve ARM64 derlemesi

## v0.2: Etkileşim ve kabuk ✅
- [x] Kendi tasarımımız olan sağ tık menüsü (Windows 11 tarzı, simge satırı ve bağlama göre değişen komutlar)
- [x] Kes, kopyala, yapıştır: Windows panosu üzerinden, Dosya Gezgini ile iki yönlü
- [x] Sürükle-bırak: paneller arasında taşı (Ctrl basılıyken kopyala); uygulamanın dışına ve dışından da çalışır
- [x] Uygulama menüsü: Dosya, Düzen, İşlemler, Görünüm, Pencere, Yardım
- [x] Seçime göre değişen şerit (ribbon) ve tür etiketi
- [x] İsteğe bağlı klasör ağacı ve önizleme paneli
- [x] Önizleme: görsel, PDF (ilk sayfa), metin/kod, müzik (hızlı çal), video
- [x] Üzerine gelince hızlı bakış: görsel, video, PDF; gecikmesi ayarlanabiliyor
- [x] Aydınlık / karanlık / sisteme göre tema
- [x] Sistem tepsisi: kapatınca arka planda çalışma (varsayılan açık), tepsiye küçültme, tepsi menüsü
- [x] İndirme yöneticisi (varsayılan kapalı): ayrı pencere, duraklat/devam, alt çubukta takip, görev çubuğunda ilerleme
- [x] Konumu ayarlanabilen modern bildirim kutusu
- [x] Ayarlar, Hakkında, Klavye kısayolları, Özellikler pencereleri

## v0.3: Önizleme ve yerleşim ✅
- [x] Office belgeleri (Word, Excel, PowerPoint, Visio, Outlook…) Windows önizleyicisiyle, sayfa sayfa
- [x] PDF'lerde sayfa gezinme (düğmeler ve fare tekeri), görsellerde yakınlaştırma
- [x] Sağ tık menüsü tıklanan noktadan açılıyor; boş alanda "Yeni" alt menüsü (Dosya Gezgini'nin şablonları)
- [x] Paneller arası sürüklenebilir ayırıcılar (çift tık: varsayılan)
- [x] Pencere konum/boyutu, panel genişlikleri, son klasörler hatırlanıyor; ayarlardan sıfırlama
- [x] Hata günlüğü (Yardım → Hata günlüğünü aç), beklenmeyen hatalarda uygulama kapanmıyor
- [x] Önizlemede her şey panel genişliğine sığıyor; PDF sayfaları alt alta, aşağı kaydırarak
- [x] Görünüm menüsünden açılıp kapanan sürücü çubuğu (doluluk göstergeli, tıklayınca aktif panelde açar)

## v0.3.x: Commander temelleri
- [ ] Sekmeler (Ctrl+T, Ctrl+W, sekmeyi diğer panele sürükleme)
- [ ] Tıklanabilir yol çubuğu (breadcrumb)
- [ ] Insert/Space ile işaretleme, renkli işaretli dosyalar
- [ ] Sıralama (ad, uzantı, boyut, tarih) ve sütun genişliği
- [ ] Gerçek dosya simgeleri (shell icon), klasör izleme (FileSystemWatcher)
- [ ] Bir klasörün üzerine bırakınca o klasöre taşıma
- [ ] Windows'un kendi sağ tık menüsünü "Daha fazla seçenek" altında gösterme
- [ ] Tarayıcı eklentisi veya `zfc-download:` protokolü ile indirmeleri devralma
- [ ] Tek örnek (single instance): ikinci açılış mevcut pencereyi öne getirsin
- [ ] Ayarların kalıcı olması (son klasörler, panel başına görünüm)

## v0.4: Önizleme ve düzenleme
- [ ] Önizle ve düzenle ekranı: bölünmüş görünüm, sözdizimi renklendirme, kaydetme
- [ ] CSV tablo önizlemesi
- [ ] Hızlı bakış kartında sabitleme (P) ve Space ile tam ekran
- [ ] Video sahne şeridi

## v0.4.x: Görünümler
- [ ] Detay, Liste, Simgeler ve Galeri modları, panel başına ayrı mod
- [ ] Ctrl + fare tekeri ile boyutlandırma (5 kademe)
- [ ] Küçük resim önbelleği (Windows thumbnail cache ve kendi disk önbelleğimiz)

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
