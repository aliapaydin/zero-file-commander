using System.Security.Cryptography;
using System.Text;

namespace ZeroFileCommander.Services;

/// <summary>
/// Uygulama açılış kilidi: 4 haneli PIN. PIN düz metin saklanmaz; tuzlu PBKDF2-SHA256 özeti tutulur.
/// Art arda yanlış girişlerde giderek uzayan bekleme uygulanır.
/// </summary>
public static class AppLock
{
    private const int Iterations = 210_000;
    private const string HashKey = "Lock.PinHash";
    private const string SaltKey = "Lock.PinSalt";

    private static int _failures;
    private static DateTime _blockedUntil = DateTime.MinValue;

    public static bool IsEnabled => AppSettings.Current.GetString(HashKey).Length > 0;

    public static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsAsciiDigit);

    /// <summary>Özet arka planda hesaplanır; ayar çağıran (arayüz) iş parçacığında yazılır.</summary>
    public static async Task SetPinAsync(string pin)
    {
        if (!IsValidPin(pin)) throw new ArgumentException(Loc.T("PIN 4 rakam olmalı."), nameof(pin));
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = await Task.Run(() => Derive(pin, salt));
        AppSettings.Current.SetString(SaltKey, Convert.ToBase64String(salt));
        AppSettings.Current.SetString(HashKey, Convert.ToBase64String(hash));
        _failures = 0;
    }

    public static void Disable()
    {
        AppSettings.Current.SetString(HashKey, string.Empty);
        AppSettings.Current.SetString(SaltKey, string.Empty);
        _failures = 0;
    }

    /// <summary>Bekleme süresi dolmadıysa kalan süre; yoksa null.</summary>
    public static TimeSpan? Cooldown => DateTime.UtcNow < _blockedUntil ? _blockedUntil - DateTime.UtcNow : null;

    public static bool Verify(string pin)
    {
        if (!IsEnabled) return true;
        if (Cooldown is not null || !IsValidPin(pin)) return false;
        try
        {
            var salt = Convert.FromBase64String(AppSettings.Current.GetString(SaltKey));
            var expected = Convert.FromBase64String(AppSettings.Current.GetString(HashKey));
            if (CryptographicOperations.FixedTimeEquals(Derive(pin, salt), expected))
            {
                _failures = 0;
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        // 5 yanlıştan sonra 30 sn, sonra her yanlışta iki katı (en çok 15 dk).
        _failures++;
        if (_failures >= 5)
            _blockedUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, _failures - 5)));
        return false;
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, 32);
}
