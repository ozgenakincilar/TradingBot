# v4 ADX trend-kalite filtresi tasarımı ve ön kaydı

**Durum:** Tasarım kilitli ve uygulanmış; 2022 historical validation sonucunda reddedildi

**Tarih:** 2026-07-27

## 1. Problem ve hipotez

v2 ve v3 kanıtları, ana zararın giriş sonrasında sürdürülemeyen hareketlerden geldiğini gösterdi. 2023 v3 validation'da `signal-ema-hysteresis-cross-down` ile kapanan 76 işlemin hiçbiri kârlı değildi. v3 trailing çıkışı bazı kazançları korudu fakat yeni re-entry döngüleri toplam işlem ve maliyeti artırdı.

v4 hipotezi exit kuralını tekrar ayarlamaz. Amaç, yalnız yönü yukarı ve trend gücü yeterli olan `1H` rejimlerde mevcut v2 girişine izin vererek yatay/zayıf piyasa işlemlerini azaltmaktır.

## 2. Kilitli strateji sözleşmesi

v4, reddedilmiş v3'ten değil v2'den çatallanır:

- Spot, long/flat, kaldıraçsız ve `%10` quote allocation değişmez.
- Signal timeframe `15m`, trend timeframe `1H` kalır.
- EMA20 giriş/çıkış ve `30 bps` hysteresis v2 ile aynıdır.
- `1H close > EMA200` yön filtresi ve `%2` FOMO guard aynıdır.
- v3 cooldown ve trailing profit-protection v4'e taşınmaz.
- Yeni tek davranış, **yalnız flat → long girişinde** `1H ADX(14) >= 25` koşuludur.
- Açık long pozisyonda ADX düşüşü exit üretmez. Exit önceliği ve reason code'ları v2 ile aynıdır.
- ADX giriş engeli reason code'u `trend-strength-blocked` olur.

`14` ve `25`, bu repository sonuçlarından optimize edilmemiş klasik ADX periyodu ve trend-gücü eşiğidir. ADX yön ölçmez; long yönü mevcut EMA200 filtresi belirler. Böylece aynı görevi yapan çok sayıda indikatör eklenmez.

## 3. Exact ADX matematiği

Yalnız tamamlanmış ve contiguous `1H` candle kullanılır. Periyot `p = 14` olarak sabittir. Candle indeksleri `i = 0..n` olsun.

Her `i >= 1` için:

```text
upMove   = High[i] - High[i-1]
downMove = Low[i-1] - Low[i]

plusDM[i]  = upMove > downMove ve upMove > 0 ise upMove, aksi halde 0
minusDM[i] = downMove > upMove ve downMove > 0 ise downMove, aksi halde 0

TR[i] = max(
  High[i] - Low[i],
  abs(High[i] - Close[i-1]),
  abs(Low[i] - Close[i-1]))
```

İlk Wilder toplamları `i=1..p` aralığıdır:

```text
smoothedTR[p]      = sum(TR[1..p])
smoothedPlusDM[p]  = sum(plusDM[1..p])
smoothedMinusDM[p] = sum(minusDM[1..p])
```

Sonraki candle'larda her toplam şu şekilde güncellenir:

```text
smoothedX[i] = smoothedX[i-1] - smoothedX[i-1] / p + X[i]
```

Her smoothing noktası için:

```text
plusDI  = 100 * smoothedPlusDM / smoothedTR
minusDI = 100 * smoothedMinusDM / smoothedTR
DX      = 100 * abs(plusDI - minusDI) / (plusDI + minusDI)
```

`smoothedTR == 0` veya `plusDI + minusDI == 0` ise ilgili `DX = 0` kabul edilir. İlk ADX, `DX[p..2p-1]` aralığındaki tam 14 DX değerinin aritmetik ortalamasıdır. Sonraki değerler:

```text
ADX[i] = (ADX[i-1] * (p - 1) + DX[i]) / p
```

Bu tanım ilk ADX için en az `2p = 28` kapalı `1H` candle ister. Stratejinin mevcut 200 candle warm-up sınırı bunu zaten aşar. Hesap yalnız `decimal` ve checked arithmetic kullanır; ara adım yuvarlanmaz. Overflow veya candle kimlik/contiguity hatası fail-closed sonuçlanır.

## 4. Karar sırası

Flat durumda:

1. `1H close > EMA200` değilse `trend-filter-blocked`.
2. `15m` EMA20 upper hysteresis cross yoksa `no-entry-signal`.
3. Pozitif candle body `%2` üstündeyse `fomo-guard-blocked`.
4. Güncel kapalı `1H ADX(14) < 25` ise `trend-strength-blocked`.
5. Tümü geçerse mevcut `signal-ema-hysteresis-cross-up` ile `EnterLong`.

Long durumda ADX hesap sonucu karar vermez. Önce trend filtresi kaybı, sonra mevcut lower-band cross değerlendirilir. Bu ayrım, zayıflayan ADX nedeniyle gereksiz erken çıkış ve re-entry döngüsü yaratmamak için tasarım invariant'ıdır.

## 5. Mimari ve kimlik sınırı

- Domain'de saf ve stateless bir `AverageDirectionalIndex` value calculator bulunur.
- `StrategyDefinition` v4 için `TrendStrengthPeriod=14` ve `MinimumTrendStrength=25` taşır; v1-v3 bu alanları sıfır taşımak zorundadır.
- Yeni configuration schema `adx-regime-v1` olur ve iki alanı manifest hash'ine dahil eder.
- v1/v2/v3 configuration, manifest, decision ve report hash yolları değişmeden kalır.
- Indicator tüm geçmişi koleksiyona almaz; mevcut bounded 200-candle trend window üzerinde çalışır.
- Araştırma komutu planlanan adıyla `validate-adx-regime-v4` olur ve yalnız v2-v4 karşılaştırır.

## 6. Test sözleşmesi

Uygulamadan önce aşağıdaki testler zorunludur:

- Sabit fiyat serisi `ADX=0` üretir ve sıfıra bölünmez.
- Kaydedilmiş bağımsız canonical fixture exact decimal ADX sonucu üretir.
- Güçlü tek yönlü seri eşik üstü, yatay/choppy seri eşik altı sonuç verir.
- İlk değer için 28'den az candle fail-closed reddedilir.
- Yanlış instrument/timeframe, gap, açık veya gelecek candle reddedilir.
- Aynı candle dizisi aynı ADX ve strategy decision üretir.
- Flat entry cross ADX düşükken engellenir; `ADX=25` dahil olmak üzere eşik ve üstü kabul edilir.
- Long pozisyonda ADX düşüşü exit üretmez; v2 exit davranışı aynen kalır.
- v1/v2/v3 kilitli hash ve ret kanıtları birebir tekrar üretilir.
- Validation raporu tek başarısız kapıda `IsAccepted=false`, CLI ise `exit 3` üretir.

## 7. Ayrı development validation ön kaydı

2023 ve 2024 verileri hipotez tasarımını etkilediği, 2025 v1 OOS sonucu daha önce gözlendiği için bu yıllar v4 seçimi için tekrar kullanılamaz. İlk v2-v4 karşılaştırması, daha önce indirilmemiş **2022 BTC-USDT** canonical verisinin yalnız train/validation bölümlerinde yapılacaktır.

2022, tasarımdan kronolojik olarak önce olduğu için final forward OOS kanıtı sayılmaz; yalnız ayrı historical robustness/development validation görevi görür. 2022 içindeki ayrılmış holdout açılmayacaktır. v4 validation'ı geçse dahi final kabul için 2026-07-27 sonrasında oluşan, tasarım anında mevcut olmayan forward OOS veri beklenir.

Önceden kilitlenen kapıların tamamı geçmelidir:

1. v4 tamamlanan trade sayısı v2'ye göre en az `%20` azalmalı.
2. Toplam fee + spread + slippage v2'ye göre en az `%20` azalmalı.
3. v4 toplam completed trade sayısı en az `30` olmalı; hareketsizlik başarı sayılamaz.
4. Net trade PnL üzerinden aggregate profit factor en az `1,10` olmalı.
5. Compounded validation net return pozitif olmalı.
6. Aynı pencerelerde maliyetli buy-and-hold benchmark excess negatif olmamalı.
7. Worst-window maximum drawdown `%5` değerini aşmamalı.
8. Validation pencerelerinin en az `%60`'ı pozitif net kapanmalı.

Bir kapı başarısızsa v4 reddedilir. ADX periyodu/eşiği veya acceptance değerleri sonuçtan sonra değiştirilmez; 2022 holdout ya da yeni forward OOS açılmaz. Aylık `%10` hedefi bu kapıları veya risk limitlerini gevşetmez.

## 8. Bu tasarımın iddia etmedikleri

- ADX haber, likidite, volatility spike veya black-swan koruması değildir.
- `ADX >= 25` kâr garantisi veya piyasanın kesin trendde olduğunun kanıtı değildir.
- Historical validation sonucu live readiness değildir.
- Lokal indikatör filtresi server-side protective stop ve reconciliation gereksinimlerinin yerine geçmez.

## 9. Uygulama kanıtı

- Domain'de checked `decimal` Wilder hesabı yapan saf `AverageDirectionalIndex` bulunur.
- v4 tanımı `TrendStrengthPeriod=14` ve `MinimumTrendStrength=25` olmadan oluşturulamaz; v1-v3 bu alanları taşıyamaz.
- v4, v3 trade context/trailing davranışına girmez ve v2 çıkış sırasını korur.
- Manifest configuration schema'sı `adx-regime-v1` olarak ayrılmıştır; mevcut v1-v3 regression testleri değişmeden geçmektedir.
- `validate-adx-regime-v4` komutu v2-v4 karşılaştırmasını train/validation ile sınırlar ve tek acceptance kapısı başarısızsa exit code `3` üretir.
- Sabit, güçlü tek yönlü, eksik warm-up, gap, entry block/allow ve açık pozisyonda ADX düşüşü testleri otomatik test kapsamındadır.

Bu uygulama kanıtı validation sonucu değildir. 2022 verisi indirilmeden veya komut çalıştırılmadan önceki ön kayıt aynen korunmuştur.

2022 historical development validation sonucu daha sonra [ayrı kanıt belgesinde](20-2022-v4-validation-kaniti.md) kaydedilmiş ve v4 reddedilmiştir. Bu bölümdeki eşikler sonuçtan sonra değiştirilmemiştir.
