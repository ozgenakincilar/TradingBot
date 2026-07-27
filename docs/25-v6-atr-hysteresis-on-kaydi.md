# v6 ATR hysteresis tasarımı ve ön kaydı

**Durum:** Tasarım kilitli ve domain/strategy implementasyonu tamamlandı; validation çalıştırılmadı

**Tarih:** 2026-07-27

## Hipotez

v5 attribution, kalan yaklaşık brüt edge'in sabit 30 bps bandı çevresindeki tekrar giriş/çıkış maliyetiyle tüketildiğini gösterdi. v6 hipotezi, mutlak sabit bandı signal timeframe volatilitesine ölçekleyerek düşük volatilitede gecikmeyi azaltmak, yüksek volatilitede gürültülü cross'ları engellemektir.

## Tek davranış değişikliği

v6, v5'in EMA20/EMA200, FOMO, ADX(14) `>=25`, strict `+DI > -DI`, long/flat exposure ve exit sırasını korur. Yalnız signal EMA hysteresis mesafesi değişir:

```text
ATR period = 14
ATR multiplier = 0,2
band distance = ATR(14) * 0,2
upper band = EMA20 + band distance
lower band = EMA20 - band distance
```

Sabit `SignalEmaHysteresisBasisPoints` v6 için zorunlu olarak sıfırdır. Entry ve exit reason kodları sırasıyla `signal-ema-atr-hysteresis-cross-up` ve `signal-ema-atr-hysteresis-cross-down` olur.

## Nedensellik ve matematik

- ATR yalnız contiguous, kapalı `15m` mumlar ve checked `decimal` ile Wilder smoothing kullanır.
- Önceki cross sınırı son signal mumunu dışlayan ATR/EMA snapshot'ından, güncel sınır güncel kapanmış mum dahil ATR/EMA snapshot'ından hesaplanır.
- Mum kapanmadan karar üretilmez; fill yine bir sonraki mumda değerlendirilir.
- ATR en az `period+1`, önceki/güncel çift snapshot en az `period+2` mum gerektirir. Mevcut 200 mum warm-up bu sınırı karşılar.
- Gap, instrument/timeframe farkı, yetersiz veri ve decimal overflow fail-closed olur.

## Sürüm ve kimlik

- v6 `SignalAtrPeriod=14`, `SignalAtrHysteresisMultiplier=0,2`, `TrendStrengthPeriod=14`, `MinimumTrendStrength=25` ve `RequirePositiveDirectionalMovement=true` taşır.
- v1-v5 ATR alanlarını sıfır taşımak zorundadır ve karar/configuration hash yolları değişmez.
- v6 strategy configuration schema `atr-hysteresis-v1` olur.
- Dinamik execution kullanıldığında ATR alanları `volatility-adjusted-twap-backtest-v1` configuration kimliğine dahil edilir.

## Validation sınırı

- 2021-2025 verileri daha önce hipotez üretiminde kullanıldığı için v6 parametre seçimi veya başarı iddiasında tekrar kullanılamaz.
- İlk ekonomik karşılaştırma v5 ve v6'yı aynı dinamik execution policy altında çalıştırmalıdır; böylece tek strateji farkı ATR bandıdır.
- Buy-and-hold benchmark dinamik maliyet/TWAP parity'si tamamlanmadan validation komutu fail-closed kalır.
- İlk kabul verisi 2026-07-27 sonrasında oluşan, önceden görülmemiş en az beş adet kesişmeyen 30 günlük forward pencere olmalıdır.
- Aylık hedef `%10` bir garanti veya acceptance eşiği değildir.

Tüm kapılar geçmelidir:

1. v6 toplam completed trade sayısı en az `30` olmalı.
2. v6 aggregate profit factor hem `1,10` veya üstü hem v5'ten yüksek olmalı.
3. v6 compounded net return pozitif olmalı.
4. Aynı dinamik maliyetli buy-and-hold benchmark'a göre excess negatif olmamalı.
5. Worst window drawdown en fazla `%5` olmalı.
6. Kârlı pencere oranı en az `%60` olmalı.
7. Toplam execution maliyeti toplam gross-before-cost kârından düşük olmalı.
8. Hiçbir pencerede pending execution veya açık remainder bulunmamalı.

Bir kapı başarısızsa v6 reddedilir. ATR period/multiplier, execution parametreleri veya acceptance kapıları sonuçtan sonra değiştirilmez. Historical başarı paper/testnet/live izni oluşturmaz.
