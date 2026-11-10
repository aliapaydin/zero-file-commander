# Gizlilik Politikası — Zero File Commander

*Son güncelleme: 11 Kasım 2026*

*(English version below.)*

Zero File Commander ("uygulama"), Ali Apaydın tarafından geliştirilen açık kaynaklı bir dosya yöneticisidir. Bu politika, uygulamanın hangi bilgileri nasıl işlediğini açıklar.

## Kısaca
**Uygulama kişisel veri toplamaz, kimseyle paylaşmaz ve geliştiriciye hiçbir veri göndermez.** Telemetri, reklam, analiz ya da izleme yoktur.

## Bilgisayarınızda saklananlar
Aşağıdakiler yalnızca sizin bilgisayarınızda, uygulamanın kendi veri klasöründe tutulur:
- **Ayarlar:** tema, pencere ve panel konumları, açık sekmeler, son klasörler.
- **Kayıtlı bağlantılar:** sunucu adı, bağlantı noktası, kullanıcı adı, başlangıç klasörü ve onaylanmış SFTP sunucu anahtarı parmak izi.
- **Parolalar:** Windows Kimlik Bilgisi Kasası'nda (Windows Credential Locker) saklanır. Uygulamanın ayar dosyasına yazılmaz.
- **PIN kilidi:** PIN'in kendisi saklanmaz. Yalnızca geri çözülemeyen bir özeti (PBKDF2-SHA256) tutulur.
- **Hata günlüğü:** Uygulama hata verirse teknik ayrıntılar yerel bir metin dosyasına yazılır. Bu dosya otomatik olarak hiçbir yere gönderilmez.
- **Geçici dosyalar:** Arşiv içi ve uzak sunucudaki dosyaları açmak veya önizlemek için oluşturulan kopyalar. Bunlar uygulama kapanırken silinir. O sırada başka bir programda açık olanlar sonraki açılışta temizlenir. Arşivden panoya kopyalananlar, başka bir yere yapıştırabilmeniz için bir gün tutulur.

Uygulamayı kaldırdığınızda ya da Windows Ayarlar'dan sıfırladığınızda bu veriler silinir. Parolalar Windows Kimlik Bilgisi Yöneticisi'nden de silinebilir.

## İnternet bağlantısı
Uygulama internete yalnızca **sizin başlattığınız** işlemler için bağlanır:
- Kaydettiğiniz bir sunucuya (SFTP, FTP/FTPS, ağ paylaşımı) bağlandığınızda, doğrudan o sunucuyla.
- İndirme yöneticisini açıp bir indirme başlattığınızda, yalnızca o adresle.
- Yardım menüsünden GitHub sayfasını açtığınızda, varsayılan tarayıcınızla.

Bu bağlantılarda geliştiriciye ya da üçüncü bir tarafa veri iletilmez.

## Dosyalarınız
Uygulama dosyalarınızı yalnızca sizin isteğiniz üzerine (açma, önizleme, kopyalama vb.) okur ve değiştirir. Dosya içerikleri hiçbir yere yüklenmez. Önizlemeler bilgisayarınızda üretilir.

## Çocuklar
Uygulama genel kullanıma yöneliktir ve çocuklardan bilerek bilgi toplamaz. Zaten hiç kimseden bilgi toplamaz.

## Değişiklikler
Bu politika değişirse güncel hali bu sayfada yayımlanır ve tarih güncellenir.

## İletişim
Sorularınız için: [GitHub Issues](https://github.com/aliapaydin/zero-file-commander/issues)

---

# Privacy Policy — Zero File Commander

*Last updated: November 11, 2026*

Zero File Commander ("the app") is an open-source file manager developed by Ali Apaydın.

**The app does not collect, share or send any personal data to the developer or third parties.** There is no telemetry, advertising, analytics or tracking.

- **Stored locally only:**
  - Settings, saved connections (host, port, user name, start folder, SFTP host key fingerprint) and a local error log.
  - Passwords, kept in the Windows Credential Locker.
  - An app PIN stored only as an irreversible PBKDF2-SHA256 hash.
  - Temporary copies created for previews, deleted when the app closes (or on the next start if still in use).
- **Network access** happens only for actions you start:
  - connecting to a server you configured (SFTP, FTP/FTPS, SMB),
  - downloads you start in the optional download manager,
  - opening the GitHub page from the Help menu.
- **Your files** are read or changed only when you ask, and are never uploaded to the developer.
- **Contact:** [GitHub Issues](https://github.com/aliapaydin/zero-file-commander/issues)
