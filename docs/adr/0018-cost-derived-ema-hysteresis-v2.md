# ADR-0018: Cost-derived EMA hysteresis v2 araştırma adayı

**Durum:** Araştırma kararı tamamlandı — aday validation'da reddedildi
**Tarih:** 2026-07-26

## Bağlam

`btc-usdt-long-flat-baseline/v1`, gerçek 2025 OOS değerlendirmesinde 421 tamamlanan trade ve yaklaşık eşit `81,56 USDT` fee/spread/slippage maliyeti üreterek beş pencerenin tamamında negatif kalmıştır. Ortalama brüt getiri yaklaşık sıfırken net sonuç `-%4,97` olmuştur. Bu kanıt v1'i reddeder; ancak gözlemlenmiş OOS değerlerine göre yeni parametre aramak overfitting olur.

Mevcut execution policy'nin teorik iki yönlü sürtünmesi `60 bps`tir:

- İki yön komisyon: `10 + 10 = 20 bps`.
- Toplam sentetik spread geçişi: `20 bps`.
- İki yön slippage: `10 + 10 = 20 bps`.

## Karar

- Aynı strateji kimliğinin v2 sürümü, signal EMA20 çevresinde simetrik `30 bps` hysteresis kullanır.
- Flat durumda entry yalnız close, `EMA20 × 1,003` üst bandını aşağıdan yukarı geçtiğinde aday olur.
- Long durumda signal exit yalnız close, `EMA20 × 0,997` alt bandını yukarıdan aşağı geçtiğinde oluşur.
- `1H close <= EMA200` trend-filter exit'i bandı beklemeden fail-safe önceliğini korur.
- EMA20/EMA200, `%2` FOMO guard, long/flat, spot-only ve tüm execution/risk varsayımları değişmez.
- `30 bps`, 2025 fiyat sonucundan optimize edilmez; kabul edilmiş round-trip maliyetinin yarısından türetilir.
- v1 hysteresis değeri zorunlu olarak sıfırdır. v1 karar reason code'ları ve configuration SHA-256 hesaplama şekli aynen korunur.
- Hysteresis kullanan konfigürasyonlar ayrı `cost-aware-hysteresis-v1` configuration hash zarfına girer; eşik değişikliği yeni hash üretir.
- 2025-07-30–2025-12-27 OOS pencereleri v2 parameter selection, validation veya başarı iddiasında kullanılamaz.

## Train/validation kabul kapısı

v2 yeni ve görülmemiş final OOS verisine yalnız aynı ayrılmış train/validation döneminde v1'e karşı aşağıdaki koşulların tümünü sağlarsa açılır:

- Tamamlanan trade ve toplam execution maliyetinde en az `%30` azalma.
- Pozitif birleşik net getiri.
- Negatif olmayan benchmark excess net getiri.
- Her pencerede maksimum drawdown en fazla `%5`.
- Pencerelerin en az `%60`ında pozitif net getiri.

Koşullardan biri sağlanmazsa v2 reddedilir. Eşik gevşetilmez ve OOS açılmaz.

## Sonuçlar

- 2024 development validation çalışmasında trade `%77,19`, execution maliyeti `%76,39` azaldı; ancak bileşik net getiri `-%5,38`, benchmark excess `-%10,20` ve kârlı pencere oranı `%0` olduğu için aday reddedildi.
- Yeni final OOS açılmadı; v2 paper/testnet/live profile'a terfi ettirilmedi. Ayrıntılı hash ve pencere kanıtı [2024 v2 validation belgesindedir](../15-2024-v2-validation-kaniti.md).
- Hysteresis doğal EMA çevresi gürültüsünde gereksiz cross sayısını azaltmayı hedefler.
- Daha az işlem nedeniyle fırsat kaçırma ve trend girişinin gecikmesi beklenen trade-off'tur.
- Kodun ve testlerin tamamlanması stratejiyi paper/testnet/live adayı yapmaz; ayrı validation ve kilitli yeni OOS kanıtı gerekir.
- Aylık `%10` hedefi eşik, karar veya kabul matematiğine girmez.

## Alternatifler

- 2025 OOS üzerinde en iyi bandı taramak, look-ahead/overfitting nedeniyle reddedildi.
- Cooldown süresi eklemek, ekonomik maliyetten doğrudan türetilemeyen ikinci bir parametre yarattığı için bu sürüme alınmadı.
- ADX/Bollinger regime filtresi eklemek, aynı anda birden fazla hipotezi değiştirip nedenselliği belirsizleştireceği için sonraya bırakıldı.
- Maliyet varsayımını düşürmek, gerçekçi execution kanıtını yapay biçimde iyileştireceği için reddedildi.
