# v5 DMI yön doğrulaması tasarımı ve ön kaydı

**Durum:** Tasarım kilitli; uygulanmadı ve veri üzerinde çalıştırılmadı

**Tarih:** 2026-07-27

## 1. Problem ve hipotez

v4, `ADX(14) >= 25` ile güçlü trendleri seçerek trade ve maliyeti `%42+` azalttı fakat 72 işlemin 60'ı zarar etti. Attribution kanıtı, execution maliyeti tamamen çıkarılsa bile sonucun negatif kalacağını gösterdi. ADX trend gücünü ölçer, yön ölçmez; yavaş `close > EMA200` filtresi güçlü trendin entry anında kısa vadede yukarı yönlü olduğunu garanti etmez.

v5 hipotezi, v4'ün trend gücü filtresine aynı Wilder Directional Movement System içindeki `+DI > -DI` yön koşulunu eklemektir. Amaç güçlü fakat kısa vadede aşağı yönlü trendlerde long girişini engellemektir.

## 2. Kilitli strateji sözleşmesi

v5, v4'ten yalnız bir davranış değişikliğiyle çatallanır:

- Spot, long/flat, kaldıraçsız ve `%10` quote allocation değişmez.
- `15m` EMA20, `30 bps` hysteresis, `%2` FOMO guard ve `1H close > EMA200` aynıdır.
- `1H ADX(14) >= 25` entry-only güç filtresi aynıdır.
- Yeni tek koşul, flat → long girişinde güncel kapalı `1H` candle için `plusDI > minusDI` olmasıdır.
- Eşitlik (`plusDI == minusDI`) girişe izin vermez.
- DMI yön değişimi açık long pozisyonda exit üretmez; v2/v4 exit sırası aynen korunur.
- Engellenen giriş reason code'u `trend-direction-blocked` olur.
- v3 cooldown/trailing davranışı taşınmaz.

## 3. Exact DMI matematiği

`+DM`, `-DM`, true range ve Wilder smoothing, [v4 ön kaydındaki](19-v4-adx-rejim-on-kaydi.md) exact formülle aynıdır. Güncel smoothing noktasında:

```text
plusDI  = smoothedTR == 0 ise 0, aksi halde 100 * smoothedPlusDM / smoothedTR
minusDI = smoothedTR == 0 ise 0, aksi halde 100 * smoothedMinusDM / smoothedTR
```

Hesap yalnız contiguous, kapalı `1H` candle, checked `decimal` ve ara yuvarlama olmadan yapılır. v5 aynı hesap geçişinde ADX, `plusDI` ve `minusDI` üretir; ayrı bir indikatör zinciriyle aynı seriyi tekrar dolaşmaz. Minimum 28 candle ve bounded 200-candle window korunur.

## 4. Karar sırası

Flat durumda:

1. `1H close > EMA200` değilse `trend-filter-blocked`.
2. `15m` upper hysteresis cross yoksa `no-entry-signal`.
3. Pozitif candle body `%2` üstündeyse `fomo-guard-blocked`.
4. `ADX(14) < 25` ise `trend-strength-blocked`.
5. `plusDI <= minusDI` ise `trend-direction-blocked`.
6. Tümü geçerse `signal-ema-hysteresis-cross-up` ile `EnterLong`.

Long durumda ADX ve DI değerleri exit kararına katılmaz. Trend filtresi kaybı ve lower hysteresis cross v2 sırasıyla çalışır.

## 5. Mimari ve kimlik sınırı

- Mevcut ADX calculator, `AverageDirectionalIndexResult` yerine ADX/+DI/-DI taşıyan geriye uyumlu bir directional result üretecek şekilde genişletilir.
- `StrategyDefinition` v5 için v4 ile aynı `TrendStrengthPeriod=14`, `MinimumTrendStrength=25` ve yeni `RequirePositiveDirectionalMovement=true` alanını taşır.
- v1-v4 bu boolean alanı `false` taşımak zorundadır.
- Yeni strategy configuration schema `dmi-direction-v1` olur; v1-v4 configuration/manifest/report hash yolları değişmez.
- Planlanan research komutu `validate-dmi-direction-v5` olur ve yalnız v4-v5 karşılaştırır.

## 6. Zorunlu test sözleşmesi

- Tek yönlü yükselen seri `plusDI > minusDI`, düşen seri `minusDI > plusDI` üretir.
- Sabit seride her iki DI ve ADX sıfır olur.
- Exact eşitlik entry'yi engeller; strict `plusDI > minusDI` entry'ye izin verir.
- Eksik warm-up, gap, identity, açık/gelecek candle ve overflow fail-closed olur.
- DMI düşüşü veya yön değişimi açık pozisyonda exit üretmez.
- v1-v4 kilitli configuration ve manifest hash kanıtları birebir tekrar üretilir.
- Validation tek başarısız kapıda `IsAccepted=false` ve process exit code `3` üretir.

## 7. 2021 historical validation ön kaydı

İlk v4-v5 karşılaştırması, henüz indirilmemiş **2021 BTC-USDT** canonical verisinin yalnız train/validation bölümlerinde yapılacaktır. 2021 holdout açılmayacaktır. 2021 tasarımdan önce oluştuğu için sonuç yalnız historical development robustness sayılır, forward OOS değildir.

Tüm kapılar geçmelidir:

1. v5 completed trade sayısı en az `30` olmalı.
2. v5 completed trade sayısı v4'ten düşük olmalı; yön filtresi etkisiz başarı sayılamaz.
3. v5 toplam execution maliyeti v4'ten düşük olmalı.
4. v5 aggregate net trade profit factor hem `1,10` veya üstü hem de v4'ten yüksek olmalı.
5. v5 compounded validation net return pozitif olmalı.
6. Maliyetli buy-and-hold benchmark excess negatif olmamalı.
7. Worst-window maximum drawdown `%5` değerini aşmamalı.
8. Validation pencerelerinin en az `%60`'ı pozitif net kapanmalı.

Bir kapı başarısızsa v5 reddedilir. Period, ADX eşiği, DI karşılaştırması veya acceptance kapıları sonuçtan sonra değiştirilmez. 2021 holdout açılmaz. v5 geçse bile final kabul için 2026-07-27 sonrasında oluşan gerçek forward OOS veri gerekir.

## 8. İddia sınırı

- `+DI > -DI` kâr garantisi veya trend dönüşü tahmini değildir.
- DMI lagging bir fiyat türevidir; haber, likidite ve spike koruması değildir.
- Historical validation paper/testnet/live izni değildir.
- Aylık `%10` hedefi bu kapıları veya risk sınırlarını gevşetmez.
