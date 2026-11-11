<p align="center">
  <img src="assets/brand/zfc-logo-256.png" width="112" alt="Zero File Commander logosu">
</p>

<h1 align="center">Zero File Commander</h1>

<p align="center">
  Klasik çift panelli dosya yöneticilerinin gücü, modern Windows 11 tadında.<br>
  <sub>The power of a classic dual-pane commander, with a modern Windows 11 feel.</sub>
</p>

<p align="center">
  <a href="https://github.com/aliapaydin/zero-file-commander/actions/workflows/build.yml"><img src="https://github.com/aliapaydin/zero-file-commander/actions/workflows/build.yml/badge.svg" alt="Derleme"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/lisans-MIT-1f6fd6" alt="MIT Lisansı"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-1f6fd6" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/sürüm-0.9.0-1f6fd6" alt="Sürüm 0.9.0">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

<p align="center">
  <a href="#-öne-çıkanlar">Öne çıkanlar</a> ·
  <a href="#-özellikler">Özellikler</a> ·
  <a href="#️-klavye-kısayolları">Kısayollar</a> ·
  <a href="#-kurulum-ve-derleme">Kurulum</a> ·
  <a href="#-yol-haritası">Yol haritası</a> ·
  <a href="#-katkı">Katkı</a>
</p>

---

**Zero File Commander**, Total Commander'ın hızını ve çift panel düzenini, Windows 11'in Fluent tasarımıyla buluşturan açık kaynaklı bir dosya yöneticisi. Klavyeyle hızlı çalışmak isteyenler için tasarlandı ama fareyle de rahat kullanılıyor: sürükle-bırak, sağ tık menüsü, şerit menü ve her yerde önizleme.

> **Durum:** Aktif geliştirme aşamasında (v0.9.0). Günlük kullanıma büyük ölçüde hazır; hatalar ve öneriler için [issue açabilirsin](https://github.com/aliapaydin/zero-file-commander/issues/new/choose). Neyin bitip neyin sırada olduğu [yol haritasında](docs/ROADMAP.md).

## ✨ Öne çıkanlar

| | |
|---|---|
| 🗂️ **Çift panel, sekmeler** | İki panel, her panelde sekmeler, klavye öncelikli kullanım (F2–F8, Tab, Insert…) |
| 🚚 **Akıllı kopyalama kuyruğu** | Duraklat / sürdür, hız ve kalan süre; çakışmalar işi durdurmaz, sona bırakılır |
| 🖥️ **Çoklu pencere ve monitör** | Ctrl+N ile yeni pencere, pencereler arası gönderme, dosya toplama rafı |
| 👁️ **Her şeyi önizle** | Görsel, video, müzik, PDF, Word/Excel/PowerPoint, kod (renkli), CSV/Parquet, Jupyter, Power BI |
| 🌐 **Bağlantılar** | NAS/ağ paylaşımı (SMB), SFTP, FTP/FTPS, OneDrive / Google Drive / Dropbox klasörleri |
| 🔧 **Geliştirici dostu** | Git durumu, sözdizimi renklendirme, "VS Code ile aç", komut paleti (Ctrl+K) |
| 🔒 **Güvenli varsayılanlar** | Silme her zaman Geri Dönüşüm Kutusu'na; kalıcı silme yalnızca onayla; isteğe bağlı PIN kilidi |

## 📋 Özellikler

### Dosya yönetimi
- **Çift panel ve sekmeler:** Her panelde birden çok sekme (Ctrl+T / Ctrl+W / Ctrl+Tab). Sekmeler, panel genişlikleri ve pencere konumu kapanışta hatırlanır.
- **Klavye öncelikli:** F2–F8 klasik commander tuşları. Insert ile işaretleme, Num +/−/* ile desene göre seçme. Harf yazınca liste anında filtrelenir.
- **Yol çubuğu:** Tıklanabilir yol (breadcrumb). Ctrl+L ile yolu yazarak düzenleyebilirsin.
- **Sürükle-bırak:** Paneller arasında taşı (Ctrl ile kopyala). Dosya Gezgini'ne, masaüstüne veya başka uygulamalara sürükleyip bırakabilirsin.
- **Windows 11 tarzı sağ tık menüsü:** Menü tıklanan noktada açılır. Boş alana sağ tıklayınca **Yeni** alt menüsü çıkar (klasör, metin belgesi, Office dosyaları; Windows'un "Yeni" listesindeki her şey).
- **Şerit menü:** Seçime göre değişen komutlar. Menü çubuğu ve komut paleti (Ctrl+K) her komuta ulaşır.
- **ZIP arşivleri:** Arşivin içine klasör gibi gir, çıkar, ekle, sil. Türkçe karakterli eski ZIP adları da doğru görünür.
- **Toplu yeniden adlandırma (Ctrl+M):**
  - Şablon etiketleri: `[N]` ad, `[C]` sayaç, `[D]` tarih, `[T]` saat, `[P]` üst klasör
  - Bul/değiştir (regex destekli) ve harf düzeni seçenekleri
  - Canlı önizleme ve geri alma
- **Klasör karşılaştırma ve eşitleme:**
  - Farklı dosyalar iki panelde işaretlenir.
  - Eşitleme penceresinde her satırın yönünü sen seçersin. Eşitleme hiçbir dosyayı silmez.
- **Arama (Alt+F7 / Ctrl+Shift+F):** Ad deseni ve içerikte arama. Sonuçlar bulundukça listelenir; "Panelde göster" ile sonuca gidersin.
- **Klasör ağacı ve sürücü çubuğu:** Görünüm menüsünden açılıp kapanır. Sürücü çubuğu doluluk oranlarını gösterir.

### Kopyalama ve taşıma
- **Kendi aktarım kuyruğu:** İşler sırayla yürür. Her biri duraklatılabilir, sürdürülebilir ya da iptal edilebilir. Hız, kalan süre ve görev çubuğunda ilerleme görünür.
- **Bloklamayan çakışma çözümü:** Hedefte aynı adlı dosya varsa diğer dosyalar kopyalanmaya devam eder. Çakışanlar sona bırakılır ve bildirimden tek tıkla çözülür:
  - "Daha yeniyse değiştir"
  - "Atla"
  - "Üzerine yaz"
  - "İkisini de tut"
- **Güvenli yazma:** Dosyalar önce geçici bir adla yazılır, tamamlanınca yerine taşınır. Yarım kalmış kopya bırakmaz; tarih ve öznitelikler korunur.
- İstersen Ayarlar'dan kuyruğu kapatıp Windows'un kendi kopyalama penceresini kullanabilirsin.

### Çoklu pencere ve monitör
- **Yeni pencere (Ctrl+N):** İkinci monitörde ayrı bir pencere aç. Pencereler arasında sürükle-bırak yap ya da **Ctrl+Shift+→** ile seçimi diğer pencereye gönder.
- **Raf (Ctrl+Shift+S):** Her zaman üstte duran küçük bir toplama alanı. Farklı klasörlerden dosyaları buraya bırak, sonra hepsini birden istediğin yere sürükle.

### Önizleme
- **Önizleme paneli (Ctrl+Q):** Panel genişliğine sığar, aşağı doğru kaydırılır.

  | Tür | Nasıl görünür |
  |---|---|
  | Görsel, video, müzik | Hızlı çalma (Space) |
  | PDF | Sayfa sayfa |
  | Word, Excel, PowerPoint | Windows'un kendi önizleyicisiyle, sayfa sayfa |
  | Klasör | İçeriği soluk küçük resimler olarak |

- **Üzerine gelince önizleme:** Görsel, video ve PDF dosyalarının üzerinde durunca küçük bir önizleme kartı açılır.
- **Renkli kod önizlemesi:** VS Code Dark+/Light+ renklerine yakın sözdizimi renklendirme. Desteklenen diller:
  - **Genel:** Python, SQL, C#, JavaScript/TypeScript, Java/Kotlin, C/C++, Go, Rust, Swift
  - **İşaretleme ve yapılandırma:** HTML/XML/XAML, CSS, JSON, YAML, INI/TOML, Markdown
  - **Betikler:** PowerShell, Bash, Batch, R, Dockerfile
  - **Power BI:** DAX, TMDL, Power Query (M)
- **Jupyter not defterleri (.ipynb):** Hücreler sırayla, kod renkli, çalışma numaralarıyla. Metin, hata ve grafik (PNG) çıktıları gösterilir.
- **Veri dosyaları:** CSV / TSV / Parquet tablo olarak açılır.
  - Ayraç (virgül, noktalı virgül, sekme) ve kodlama (UTF-8, Türkçe ANSI) kendiliğinden tahmin edilir.
  - Sütun türleri belirlenir; ilk 300 satır ve toplam satır sayısı gösterilir.
- **Power BI yapı özeti:**
  - **PBIX / PBIT:** sayfalar, görsel türleri, bağlantılar, Power Query sorguları
  - **PBIP / model.bim / TMDL:** tablolar, ölçüler (DAX kodlarıyla), hesaplanmış sütunlar, ilişkiler
- **VS Code ile aç:** Kod, metin, not defteri ve klasörler için önizlemede düğme, sağ tık menüsünde ve komut paletinde seçenek. VS Code kurulu değilse pasif görünür.

### Bağlantılar
- **Ağ paylaşımı / NAS (SMB):** Kullanıcı adı ve parolayla kaydet, tek tıkla aç (örnek: Home Assistant'ın Samba paylaşımı). Ulaşılamayan sunucular arayüzü dondurmaz.
- **SFTP, FTPS, FTP:** Sunucuda klasör gibi gezin.
  - F5/F6 ile indir veya yükle; aktarımlar aynı kuyrukta ilerler.
  - Yeniden adlandırma, yeni klasör ve silme desteklenir. Sunucuda Geri Dönüşüm Kutusu olmadığı için silmeden önce kırmızı onay sorulur.
  - Uzak dosyalar açılırken veya önizlenirken geçici, salt okunur bir kopyaya iner.
- **Bulut klasörleri:** OneDrive, Google Drive, Dropbox, iCloud ve Box kendiliğinden bulunur ve "Bu bilgisayar"da listelenir. Yalnızca bulutta duran dosyalar ☁ işaretiyle gösterilir; önizleme bunları sen istemeden indirmez.
- **Güvenlik:**
  - Parolalar **Windows Kimlik Bilgisi Kasası**'nda saklanır; ayar dosyasına asla yazılmaz.
  - SFTP sunucu anahtarı ilk bağlantıda onaylatılır; sonradan değişirse bağlanmadan önce uyarır.
  - FTPS'te yalnızca geçerli sertifikalar kabul edilir.

### Git
- Bir depo klasörüne girince yol çubuğunda **dal adı**, ileri/geri sayısı (↑2 ↓1) ve değişiklik sayısı görünür.
- Satırlarda durum işaretleri yer alır: **M** değişti, **A** eklendi, **D** silindi, **U** izlenmiyor, **!** çakışma. Klasörlerde içindeki en önemli değişikliğin rengi görünür.
- Git'in yok saydığı dosyalar soluk gösterilir. Dal düğmesinden "Değişenleri seç" ve "Depo köküne git" seçeneklerine ulaşılır.
- Dosya durumları için [Git for Windows](https://git-scm.com/) kurulu olmalı. Kurulu değilse yalnızca dal adı gösterilir.

### İndirme yöneticisi *(varsayılan olarak kapalı)*
- Kendi penceresi var. İndirmeler duraklatılıp sürdürülebilir, ilerleme görev çubuğunda görünür.
- İstersen panoya kopyalanan bağlantıları yakalar.
- Bildirim kutusunun ekranın hangi köşesinde çıkacağını seçebilirsin.

### Uygulama
- **Tema:** Aydınlık, karanlık ya da sisteme göre.
- **Sistem tepsisi:** Kapatınca arka planda çalışmayı sürdürür (kapatılabilir). Tepsi simgesine tıklayınca kaldığın yerden açılır.
- **PIN kilidi** *(varsayılan olarak kapalı)*: Ayarlar → Güvenlik bölümünden 4 haneli PIN tanımlayabilirsin. Açılışta, istersen tepsiden açarken de sorar.
  - PIN düz metin olarak değil, geri çözülemeyen bir özet olarak saklanır.
  - Art arda yanlış girişlerde bekleme süresi uzar.
- **Ayarları sıfırla:** Pencere ve panel yerleşimi ya da tüm ayarlar tek tıkla sıfırlanabilir.
- **Hata günlüğü:** Yardım → Hata günlüğünü aç. Bir sorun bildirirken çok işe yarar.

## ⌨️ Klavye kısayolları

| Kısayol | İşlev | | Kısayol | İşlev |
|---|---|---|---|---|
| `Tab` | Paneller arasında geç | | `Ctrl+K` | Komut paleti |
| `Enter` | Aç (ZIP'in içine girer) | | `Ctrl+T` / `Ctrl+W` | Yeni sekme / sekmeyi kapat |
| `Backspace` | Üst klasör | | `Ctrl+Tab` | Sonraki sekme |
| `Space` | Önizle / hızlı çal | | `Ctrl+L` | Yolu yazarak düzenle |
| `F2` | Yeniden adlandır | | `Ctrl+N` | Yeni pencere |
| `F3` / `F4` | Görüntüle / düzenle | | `Ctrl+Shift+→` | Seçimi diğer pencereye kopyala |
| `F5` / `F6` | Karşı panele kopyala / taşı | | `Ctrl+Shift+S` | Raf |
| `F7` | Yeni klasör | | `Ctrl+M` | Toplu yeniden adlandır |
| `F8` / `Del` | Geri Dönüşüm Kutusu'na gönder | | `Alt+F7` | Ara |
| `Shift+Del` | Kalıcı sil (kırmızı onay ister) | | `Ctrl+Q` | Önizleme panelini aç/kapat |
| `Ctrl+C` / `X` / `V` | Kopyala / kes / yapıştır | | `Ctrl+Shift+E` | Klasör ağacını aç/kapat |
| `Ctrl+Shift+C` | Yolu kopyala | | `Ctrl+J` | İndirmeler |
| `Alt+Enter` | Özellikler | | `Ctrl+R` | Yenile |
| `Insert` | İşaretle ve aşağı in | | `Ctrl+,` | Ayarlar |
| `Num +` / `−` / `*` | Desenle seç / kaldır / tersine çevir | | *Harf yaz* | Listeyi filtrele (`Esc` temizler) |

Fareyle **sürükle** taşır, **Ctrl** basılıyken kopyalar. Tüm liste uygulamada **Yardım → Klavye kısayolları** altında.

## 🚀 Kurulum ve derleme

Henüz Microsoft Store'da ya da hazır kurulum paketi olarak yayımlanmadı. Şimdilik kaynak koddan derleyerek çalıştırılıyor.

### Gereksinimler
- Windows 10 (sürüm 2004 / 19041) veya Windows 11, x64 ya da ARM64
- [Visual Studio 2026 veya 2022](https://visualstudio.microsoft.com/). Kurulumda **"WinUI uygulama geliştirme"** iş yükünü seç.
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- *(isteğe bağlı)* Dosya durumları için [Git for Windows](https://git-scm.com/), "VS Code ile aç" için [Visual Studio Code](https://code.visualstudio.com/)

### Adım adım
```powershell
git clone https://github.com/aliapaydin/zero-file-commander.git
cd zero-file-commander
start ZeroFileCommander.sln
```
1. Visual Studio'da üstteki platformu **x64** (ARM64 bilgisayarda **ARM64**) yap.
2. Çalıştırma profili olarak **ZeroFileCommander (Package)** seçili olsun.
3. **F5** ile derle ve çalıştır. İlk derlemede NuGet paketleri iner, birkaç dakika sürebilir.

### Güncelleme
```powershell
git pull
```
Ardından Visual Studio'da **Derle → Çözümü Yeniden Derle**. Garip derleme hataları görürsen `src\ZeroFileCommander` altındaki `bin`, `obj` ve çözüm klasöründeki `.vs` klasörlerini silip yeniden derle.

## 🧱 Teknik altyapı

| Katman | Kullanılan |
|---|---|
| Arayüz | WinUI 3, Windows App SDK 2.x, Fluent / Mica |
| Çalışma ortamı | .NET 10, tek proje MSIX paketi (x64, ARM64) |
| Dosya işlemleri | Kendi aktarım motoru, Windows Kabuğu (`SHFileOperation`, Geri Dönüşüm Kutusu) |
| Önizleme | `Windows.Data.Pdf`, Windows önizleyicileri (`IPreviewHandler`), MediaPlayer |
| Veri | [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) |
| Bağlantılar | [SSH.NET](https://github.com/sshnet/SSH.NET) (SFTP), [FluentFTP](https://github.com/robinrodricks/FluentFTP) (FTP/FTPS), Windows Kimlik Bilgisi Kasası |
| CI | GitHub Actions (her gönderimde x64 + ARM64 derlemesi) |

### Depo yapısı
```
assets/brand/            Logo (SVG / PNG)
docs/                    Yol haritası, tasarım notları, yayımlama kontrol listesi
src/ZeroFileCommander/   WinUI 3 uygulaması
  ├─ Controls/           Dosya paneli, önizleme paneli, ayırıcılar
  ├─ Views/              Pencereler ve diyaloglar (ayarlar, aktarımlar, bağlantılar…)
  ├─ Services/           Dosya işlemleri, aktarım kuyruğu, önizleme, Git, bağlantılar
  │   └─ Connections/    SFTP / FTP oturumları, kayıtlı bağlantılar, bulut klasörleri
  └─ Models/             Dosya öğesi ve dosya türleri
.github/workflows/       CI derlemesi
```

## 🔐 Gizlilik ve güvenlik

- Uygulama hiçbir veriyi dışarı göndermez; telemetri yoktur. İnternete yalnızca sen bir sunucuya bağlandığında ya da indirme başlattığında çıkar.
- Parolalar Windows Kimlik Bilgisi Kasası'nda, PIN ise geri çözülemeyen bir özet olarak saklanır.
- Silme işlemleri varsayılan olarak **Geri Dönüşüm Kutusu**'na gider. Kalıcı silme yalnızca Shift ile istenip kırmızı onay kutusu onaylanırsa yapılır.
- Ayrıntılı gizlilik politikası: [PRIVACY.md](PRIVACY.md)
- Bir güvenlik açığı bulursan lütfen herkese açık issue yerine [SECURITY.md](SECURITY.md)'deki yolu izle.

## 🗺️ Yol haritası

| Faz | Sürüm | Kapsam | Durum |
|---|---|---|---|
| 1 | v0.4 | Commander temelleri: sekmeler, geçmiş, filtre, işaretleme | ✅ |
| 2 | v0.5 | Güç araçları: ZIP, toplu yeniden adlandırma, karşılaştırma, eşitleme, arama | ✅ |
| 3 | v0.6 | Aktarım kuyruğu, çoklu pencere, raf | ✅ |
| 4 | v0.7 | CSV / Parquet, Power BI, Git | ✅ |
| 5 | v0.8 | NAS, SFTP / FTP, bulut klasörleri | ✅ |
| — | sırada | Sözdizimi renkli düzenleme ekranı, galeri görünümü, Windows'un kendi sağ tık menüsü | ⏳ |

Ayrıntılar: [docs/ROADMAP.md](docs/ROADMAP.md)

## 🤝 Katkı

Hata bildirimleri, öneriler ve pull request'ler memnuniyetle karşılanır. Türkçe ya da İngilizce yazabilirsin.

1. Bir [issue](https://github.com/aliapaydin/zero-file-commander/issues/new/choose) aç ya da var olanlardan birini seç.
2. Depoyu fork'la ve `feature/kisa-aciklama` adında bir dal oluştur.
3. Değişikliğini yap. Arayüz metinleri Türkçe, kod ve tanımlayıcılar İngilizce olsun.
4. Pull request aç; CI derlemesinin yeşil olması gerekir.

Ayrıntılar için: [CONTRIBUTING.md](CONTRIBUTING.md) · Tasarım ilkeleri: [docs/DESIGN.md](docs/DESIGN.md)

## 📄 Lisans

[MIT](LICENSE) © 2026 Ali Apaydın. Özgürce kullanabilir, değiştirebilir ve dağıtabilirsin.

<p align="center"><sub>Total Commander'a ve klavyeyle hızlı çalışmayı sevenlere selam olsun. 👋</sub></p>
