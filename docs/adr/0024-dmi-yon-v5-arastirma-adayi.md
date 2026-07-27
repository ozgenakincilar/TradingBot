# ADR-0024: DMI yön doğrulaması v5 araştırma adayı

**Durum:** Araştırma kararı tamamlandı — aday validation'da reddedildi

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
- 2021 historical development validation daha sonra tamamlandı. v5 trade sayısını `%29,2035`, execution maliyetini `%28,8959` azalttı ve profit factor'ı `0,3293` değerinden `0,4170` değerine yükseltti; ancak compounded net return `-%4,5272`, benchmark excess `-%9,2336` ve kârlı pencere oranı `%0` olduğu için sekiz kapının yalnız dördünü geçti ve reddedildi. Ayrıntılı artifact kimlikleri [ret kanıtında](../23-2021-v5-validation-kaniti.md) yer alır. Holdout açılmadı; paper/testnet/live izni verilmedi.

## Alternatifler

- ADX eşiğini optimize etmek, reddedilmiş veriye overfit olacağı için reddedildi.
- Trailing exit eklemek, v3'teki exit/re-entry karışıklığını tekrar yaratacağı için reddedildi.
- RSI/MACD/Stochastic zinciri, aynı anda çok değişken ekleyip attribution'ı bozacağı için reddedildi.
- `plusDI >= minusDI`, eşitlikte yön üstünlüğü bulunmadığı için reddedildi.
