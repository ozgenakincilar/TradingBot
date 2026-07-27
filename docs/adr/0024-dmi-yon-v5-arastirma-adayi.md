# ADR-0024: DMI yön doğrulaması v5 araştırma adayı

**Durum:** Kabul edildi

**Tarih:** 2026-07-27

## Bağlam

v4 ADX filtresi turnover ve maliyeti azalttı ancak pozitif expectancy üretmedi. İşlem attribution sonucu, ADX'in yön ölçmemesinin ve yavaş EMA200 filtresinin entry anındaki kısa vadeli yönü garanti etmemesinin izole edilmesi gereken bir hipotez olduğunu gösterdi.

## Karar

- v5, v4'ten çatallanır ve yalnız entry'de `+DI > -DI` ister.
- ADX(14)/25, EMA200, EMA20 hysteresis, FOMO ve exit kuralları değişmez.
- DI eşitliği veya negatif yön üstünlüğü `trend-direction-blocked` üretir.
- DI açık pozisyonda exit değildir.
- Configuration schema `dmi-direction-v1` olur; v1-v4 hash yolları korunur.
- İlk validation daha önce kullanılmamış 2021 train/validation verisinde, sekiz ön kayıtlı kapıyla yapılır; holdout açılmaz.

## Sonuçlar

- Güç ve yön aynı bounded Wilder geçişinden üretilir; yeni bağımsız indikatör yığını kurulmaz.
- Tek davranış değişikliği nedeniyle v4-v5 farkı kısa vadeli DMI yön koşuluna bağlanabilir.
- Uygulama veya historical başarı paper/testnet/live izni oluşturmaz.

## Alternatifler

- ADX eşiğini optimize etmek, reddedilmiş veriye overfit olacağı için reddedildi.
- Trailing exit eklemek, v3'teki exit/re-entry karışıklığını tekrar yaratacağı için reddedildi.
- RSI/MACD/Stochastic zinciri, aynı anda çok değişken ekleyip attribution'ı bozacağı için reddedildi.
- `plusDI >= minusDI`, eşitlikte yön üstünlüğü bulunmadığı için reddedildi.
