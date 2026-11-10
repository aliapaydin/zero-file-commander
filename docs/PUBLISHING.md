# Yayın kontrol listesi

## 1. Microsoft Store geliştirici hesabı
- [ ] https://storedeveloper.microsoft.com adresinden **Individual developer** olarak kaydol. Ücretsiz; kimlik kartı ve selfie ile doğrulama isteniyor.
- [ ] Doğrulama tamamlanınca Partner Center'a gir.

## 2. İsim rezervasyonu
- [ ] Partner Center → Apps and games → **New product → MSIX or PWA app**
- [ ] Ad: **Zero File Commander** (alınmışsa yedek ad: *ZeroFile Commander*)
- [ ] Product management → **Product identity** sayfasındaki değerleri `src/ZeroFileCommander/Package.appxmanifest` dosyasına yaz:
  - `Identity Name` → *Package/Identity/Name*
  - `Identity Publisher` → *Package/Identity/Publisher* (`CN=...`)
  - `PublisherDisplayName` → *Package/Properties/PublisherDisplayName*
- Kısayol: Visual Studio'da proje → *Package and Publish → Associate App with the Store* bu üç alanı kendisi doldurur.

## 3. Paket
- [ ] `Package.appxmanifest` içindeki `Version` değerini artır (Store her gönderimde daha büyük sürüm ister; son hane 0 olmalı, ör. 0.8.3.0).
- [ ] **Kolay yol (CI):** GitHub → Actions → **store-package** → *Run workflow*. Bitince sayfanın altındaki **ZeroFileCommander-StoreUpload** dosyasını indir; içindeki `.msixupload` Partner Center'a yüklenir. (`v0.8.3` gibi bir etiket gönderince de kendiliğinden çalışır.)
- [ ] Alternatif: Visual Studio → *Package and Publish → Create App Packages → Microsoft Store* → x64 ve ARM64.
- [ ] İsteğe bağlı ama önerilir: kendi bilgisayarında Windows App Certification Kit (WACK) testini çalıştır.
- Store'a giden paketleri Microsoft imzalıyor; kendi sertifikan gerekmiyor.

## 4. Mağaza sayfası
Tüm metinler hazır: **[docs/STORE_LISTING.md](STORE_LISTING.md)** (TR + EN açıklamalar, özellik listesi, arama terimleri, runFullTrust gerekçesi, yaş derecelendirmesi cevapları).
- [ ] Kısa ve uzun açıklama, özellikler, arama terimleri
- [ ] En az 1, ideal olarak 4–6 ekran görüntüsü (1920×1080 veya üstü)
- [ ] Gizlilik politikası URL'si: https://github.com/aliapaydin/zero-file-commander/blob/main/PRIVACY.md
- [ ] Kategori: *Utilities & tools*
- [ ] Yaş derecelendirme anketi
- [ ] Restricted capabilities → runFullTrust gerekçesi

## 5. Store dışı dağıtım (isteğe bağlı, sonra)
- GitHub Releases ve `winget` için kod imzalama sertifikası gerekiyor. Aksi halde SmartScreen uyarı veriyor.
- Seçenekler: Azure Trusted Signing veya bir CA'dan OV/EV sertifika.
