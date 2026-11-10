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

## Faz 1 · v0.4: Commander temelleri ✅
- [x] Sekmeler: Ctrl+T, Ctrl+W, Ctrl+Tab, sürükleyerek sıralama; sağ tıkla "Karşı panelde aç", çoğalt, diğerlerini kapat; kapatıp açınca geri gelir
- [x] Tıklanabilir yol çubuğu (breadcrumb); ✎ ya da Ctrl+L ile yazarak düzenleme
- [x] Sıralanabilir sütunlar (ad, uzantı, boyut, tarih), panel başına hatırlanır; uzantı sütunu
- [x] Windows'un gerçek dosya simgeleri (yalnızca görünen satırlar için, uzantı başına önbellek)
- [x] Canlı yenileme: klasör dışarıdan değişince liste güncellenir, seçim ve kaydırma bozulmaz
- [x] Yazarak filtreleme (Backspace siler, Esc temizler)
- [x] Insert ile işaretle ve ilerle, Num + / Num − ile desenle seç/kaldır, Num * ile tersine çevir

## Faz 2 · v0.5: Güç araçları ✅
- [x] Toplu yeniden adlandırma (Ctrl+M): şablon ([N] [N1-3] [E] [C] [D] [T] [P]), bul/değiştir (regex), harf düzeni, sayaç, canlı önizleme, çakışma kontrolü, geri alma
- [x] Klasörleri karşılaştır: farkları iki panelde işaretler
- [x] Klasör senkronizasyonu penceresi: alt klasörler, isteğe bağlı içerik karşılaştırma, satır satır yön seçimi; hiçbir şeyi silmez
- [x] ZIP arşivlerine klasör gibi girme (Enter), F5 ile çıkarma, arşive ekleme/silme, ZIP olarak sıkıştırma, Türkçe adlı eski ZIP'ler
- [x] Arama (Alt+F7 / Ctrl+Shift+F): ad deseni ve içerik, sonuçlar akarak gelir, "Panelde göster"
- [x] Komut paleti (Ctrl+K): komutlar, son klasörler, sürücüler, bu klasördeki dosyalar; ">" ve "/" süzgeçleri

## Faz 3 · v0.6: Transfer motoru ve çoklu pencere ✅
- [x] Kendi kopyalama kuyruğu: duraklat/devam, hız, kalan süre (Pencere → Aktarımlar)
- [x] Bloklamayan çakışma çözümü ("yalnızca daha yeniyse değiştir" gibi kurallar)
- [x] Yeni pencere (Ctrl+N); pencereler/monitörler arası sürükle-bırak ve Ctrl+Shift+→ ile gönderme
- [x] Raf: dosyaları toplayıp sonra bırakmak için her zaman üstte duran pencere (Ctrl+Shift+S)

## Faz 4 · v0.7: Veri ve geliştirici önizlemeleri ✅
- [x] CSV / TSV / Parquet tablo önizlemesi (ayraç ve kodlama tahmini, sütun türleri, ilk 300 satır)
- [x] Power BI meta bilgileri: PBIX/PBIT sayfaları, görseller, bağlantılar, Power Query; PBIP/TMSL/TMDL tabloları, ölçüler (DAX), ilişkiler
- [x] Git durumu: dal adı, ileri/geri, satırlarda M/A/D/U işaretleri, yok sayılanlar sönük

## Faz 5 · v0.8: Bağlantılar
- [ ] Kayıtlı ağ/NAS bağlantıları
- [ ] SFTP / FTP
- [ ] Bulut klasörleri

## Bekleyenler
- [ ] Önizle ve düzenle ekranı: sözdizimi renklendirme, kaydetme
- [ ] Hızlı bakış kartında sabitleme (P), video sahne şeridi
- [ ] Galeri/simge görünümleri, Ctrl + tekerlek ile boyut
- [ ] Windows'un kendi sağ tık menüsü ("Daha fazla seçenek")
- [ ] Tek örnek (ikinci açılış mevcut pencereyi öne getirsin)
- [ ] Tarayıcı eklentisi ile indirmeleri devralma

## v1.0: Yayın
- [ ] Erişilebilirlik ve performans turu
- [ ] Microsoft Store yayını (bkz. [PUBLISHING.md](PUBLISHING.md))
