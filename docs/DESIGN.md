# Tasarım notları

Etkileşimli mockup Claude'daki tasarım tuvalinde duruyor: **"Zero File Commander – Mockup"** (Ali'nin hesabında, özel).

## Mockup ekranları

| # | Ekran | Ne anlatıyor |
|---|---|---|
| 1 | Ana pencere | Çift panel, sekmeler, sürücü çipleri, önizleme paneli, F tuşu çubuğu, galeri modu, tema değiştirme |
| 2 | Önizle ve düzenle (F3/F4) | Bölünmüş kaynak ve işlenmiş görünüm, kaydedilmemiş satırlar, klasörde ↑↓ ile gezinme |
| 3 | Ekranlar arası transfer | Monitörler arası sürükle-bırak, kenar rafı, transfer kuyruğu |
| 4 | Komut paleti ve çakışma | Ctrl+K, bloklamayan çakışma penceresi, yan yana karşılaştırma |
| 5 | Görünümler | Detay, Liste, Simgeler, Galeri; 5 kademeli boyut; üzerine gelince önizleme |
| 6 | Hızlı bakış kartı | Zamanlama (350 ms bekleme, 120 ms belirme, 200 ms tolerans), sabitleme, video sahne şeridi |
| 7 | Logo ve marka | Logo yapısı, varyantlar, simge boyutları, renkler |

## Logo

Kodcuların kullandığı çizgili sıfır, aynı zamanda bir taşıma oku. Ok sıfırı kesip dışarı çıkıyor: dosya gecikmeden bir yerden ötekine geçiyor.

- Ana: `assets/brand/zfc-logo.svg`
- 16–32 px için kalın çizgili: `zfc-logo-small.svg`
- Gece ve tek renk varyantları, yazılı logo: `zfc-wordmark.svg`

## Renkler

| Ad | Hex | Kullanım |
|---|---|---|
| Zero Mavi | `#1F6FD6` | Vurgu, aktif panel, birincil düğme |
| Sinyal Amber | `#F2B84B` | İşaretli dosyalar, logodaki ok (aydınlık temada metin için `#A86A00`) |
| Gece | `#0D1016` | Karanlık tema zemini |
| Kâğıt | `#EEF1F5` | Aydınlık tema zemini |

Uygulama içinde mümkün olduğunca WinUI'nin tema kaynakları kullanılıyor (`AccentFillColorDefaultBrush`, `CardBackgroundFillColorDefaultBrush` …). Böylece sistem vurgu rengi ve yüksek karşıtlık modu kendiliğinden çalışıyor.

## İlkeler

1. **Klavye önce gelir.** Her komutun bir kısayolu olur. Fare ikinci yol.
2. **Hiçbir şey bloklamaz.** Kopyalama sürerken gezinme, önizleme ve çakışma çözümü devam eder.
3. **Önizleme her yerde.** Dosyayı açmadan içeriğini görmek varsayılan.
4. **Windows'a yakışır.** Mica, Fluent simgeleri, sistem teması; yabancı bir uygulama gibi durmaz.
