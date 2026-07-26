# 2025 BTC-USDT OOS Baseline Kanıtı

**Durum:** Reddedildi — strateji production/testnet adayı değildir  
**Çalıştırma tarihi:** 2026-07-26  
**Veri kaynağı:** OKX public closed-candle history

## 1. Amaç

Versioned `btc-usdt-long-flat-baseline/v1` stratejisini gerçek ve daha önce parametre seçimi için kullanılmamış OOS pencerelerde, maliyetli buy-and-hold benchmark ile karşılaştırmak. Bu çalışma getiri garantisi veya parametre optimizasyonu değildir.

## 2. Reproducibility kanıtı

| Artifact | Aralık / adet | SHA-256 |
|---|---|---|
| `BTC-USDT 15m` canonical CSV | 2025-01-01–2026-01-01 / 35.040 candle | `DB13C81E47DB149B5A8B18BC5B856C30FD4BE564D507B067DBE9974B5F5D3D34` |
| `BTC-USDT 1H` canonical CSV | 2025-01-01–2026-01-01 / 8.760 candle | `D69C45A86D976DCAECB49087E4A671AB3D1418DD413421202630F1D224A29AF4` |
| Schedule | 180d train + 30d validation + 30d OOS, rolling | `B07A74DA61A294D2826FCDF936384E903330843C1D5A6D2FA23B2EB785430998` |
| Run | seed `42`, beş bağımsız OOS pencere | `7D8645C898B4685BF303B5D337C981E972882B89EA94A98DDECA911665156006` |
| Report v2 | strategy + maliyetli benchmark | `0F719605D698F082B9172D574F6EEE6231D980B3D4BB0CE7A789590B7590D38B` |

Raw dataset ve JSON rapor `data/` altında yerel artifact olarak tutulur ve Git'e alınmaz. Aynı komut iki kez çalıştırılmış, aynı report SHA-256 üretilmiştir.

## 3. Sabit execution varsayımları

- Başlangıç: pencere başına `1.000 USDT`, bağımsız ve flat state.
- Allocation: `%10`, kaldıraç ve short yok.
- Komisyon: `%0,1`; sentetik spread: `20 bps`; slippage: `10 bps`.
- Önceki candle likiditesinin en fazla `%5`i; `100 ms` latency.
- Benchmark aynı sermaye, allocation, spread, komisyon ve slippage politikasını kullanır.

## 4. OOS sonuçları

| Pencere | OOS aralığı | Strateji net | Benchmark net | Excess | Max DD | Trade |
|---:|---|---:|---:|---:|---:|---:|
| 0 | 2025-07-30–2025-08-29 | `-%4,78` | `-%0,51` | `-%4,26` | `%4,78` | 78 |
| 1 | 2025-08-29–2025-09-28 | `-%7,56` | `-%0,32` | `-%7,25` | `%7,56` | 125 |
| 2 | 2025-09-28–2025-10-28 | `-%5,11` | `%0,35` | `-%5,46` | `%5,11` | 102 |
| 3 | 2025-10-28–2025-11-27 | `-%1,30` | `-%2,12` | `%0,82` | `%1,30` | 21 |
| 4 | 2025-11-27–2025-12-27 | `-%6,11` | `-%0,40` | `-%5,70` | `%6,22` | 95 |

Birleşik özet:

- Kârlı pencere: `0/5`.
- Benchmark'ı geçen pencere: `1/5`.
- Ortalama net getiri: `-%4,97`; median: `-%5,11`.
- Bileşik strateji getirisi: `-%22,60`; bileşik benchmark: `-%2,99`.
- Ortalama excess net getiri: `-%4,37`.
- Toplam tamamlanan trade: `421`; fill: `842`.
- Toplam fee: `81,56 USDT`; tahmini spread: `81,56 USDT`; tahmini slippage: `81,56 USDT`.
- Ortalama brüt getiri yaklaşık `-%0,08`; kaybın büyük bölümü aşırı turnover ve işlem maliyetlerinden oluşuyor.

## 5. Kabul kararı

Baseline reddedilmiştir:

- `%10` aylık stretch target'ın çok uzağındadır.
- Beş OOS pencerenin hiçbiri pozitif değildir.
- Üç pencere `%5` maksimum drawdown hard limitini aşar; en kötü değer `%7,56`dır.
- Buy-and-hold'a karşı kalıcı değer üretmez.
- Pozitif expectancy ve `1.30` profit factor kabul ölçütleri sağlanmaz.

Bu sonuç nedeniyle risk artırılmaz, kaldıraç açılmaz, canlı/testnet emir bağlantısına geçilmez ve OOS verisine bakarak v1 parametreleri sessizce değiştirilmez.

## 6. Sonraki araştırma kapısı

Yeni strateji sürümü ancak train/validation verisinde aşağıdaki hipotezler önceden tanımlanıp test edildikten sonra yeni, kilitli OOS döneminde değerlendirilebilir:

- Turnover'ı ve cross gürültüsünü azaltan giriş/çıkış filtresi.
- Tick/lot rounding ve daha gerçekçi order-book/queue replay.
- Farklı piyasa rejimlerini kapsayan, v1 OOS döneminden ayrılmış yeni final değerlendirme aralığı.
- Aylık equity segmentasyonu ve hard-limit ihlal raporu.

2025 OOS sonucu artık final kanıttır; aynı pencereler yeni parametre seçimi veya başarı iddiası için tekrar kullanılamaz.
