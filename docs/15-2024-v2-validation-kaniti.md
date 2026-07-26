# 2024 BTC-USDT v2 Validation Kanıtı

**Durum:** Reddedildi — final OOS, paper, testnet veya live adayı değildir

**Çalıştırma tarihi:** 2026-07-26

**Veri kaynağı:** OKX public closed-candle history

## 1. Amaç

`btc-usdt-long-flat-baseline/v2` için önceden kaydedilmiş 30 bps EMA hysteresis hipotezini, v1 ile aynı execution maliyetleri altında ve gözlemlenmiş 2025 OOS dönemini kullanmadan değerlendirmek. Bu çalışma yalnız parameter-selection amaçlı 2024 train/validation verisini kullanır; pencere planındaki OOS bölümleri stratejiye aktarılmaz.

## 2. Reproducibility kanıtı

| Artifact | Aralık / adet | SHA-256 |
|---|---|---|
| `BTC-USDT 15m` canonical CSV | 2024-01-01–2025-01-01 / 35.136 candle | `E24C8F4FB4B07E007DF95A3717542B43CD0DE2F357DE7014710BC715528D3521` |
| `BTC-USDT 1H` canonical CSV | 2024-01-01–2025-01-01 / 8.784 candle | `58F93EF7BABB4FF1D3D14279D924F9F1795011FEEA1A84A735D1ED0C830AB9CA` |
| v1 configuration | legacy hash zarfı | `D13E9C3EF0E0918ED9D0EE0F3986AC1DED23D8B39C2725A5F39F037E6C0AC788` |
| v2 configuration | 30 bps hysteresis | `5794EB4E941E7573852E44A95414BF951E2C8BDFC4C19A90665D6D460557D0EE` |
| Validation run | seed `20240726`, beş rolling pencere | `752AA474615107628E3D895D06DAF45EC5AFC5BECC43F60EFD6CFA943633EAE7` |
| Validation report v1 | v1-v2 + maliyetli benchmark | `C2ED45519CFD128DAE2CCF810B3CEB0A761898564E6182B90D840E91A70084DC` |

Raw datasetler `data/` altında yerel artifact olarak tutulur ve Git'e alınmaz. Çalıştırma komutu:

```powershell
dotnet run --project src/TradingBot.Research -- validate-hysteresis-v2 --instrument BTC-USDT --signal data/btc-usdt-15m-2024.csv --signal-source okx-btc-usdt-15m-2024 --trend data/btc-usdt-1h-2024.csv --trend-source okx-btc-usdt-1h-2024 --from "2024-01-01T00:00:00.0000000+00:00" --to "2025-01-01T00:00:00.0000000+00:00" --training-days 180 --validation-days 30 --oos-days 30 --mode rolling --seed 20240726
```

CLI, reddedilen aday için JSON raporu yazdıktan sonra fail-closed olarak non-zero exit code üretir.

## 3. Sabit deney sözleşmesi

- Pencere başına `180 gün train + 30 gün validation + 30 gün ayrılmış OOS`, rolling ilerleme.
- Train bölümü yalnız indicator warm-up sağlar; ekonomik ölçüm validation başlangıcında `Flat` state ile başlar.
- Ayrılmış OOS candle'ları v1 veya v2 strateji değerlendirmesine yield edilmez.
- Başlangıç sermayesi `1.000 USDT`, allocation `%10`; kaldıraç ve short yok.
- Komisyon `%0,1`, sentetik spread `20 bps`, slippage `10 bps`, latency `100 ms`.
- Benchmark aynı sermaye, allocation ve execution maliyetlerini kullanır.
- Hysteresis dışında v1 ve v2 strateji sözleşmeleri aynıdır; acceptance eşikleri çalıştırmadan önce ADR-0018'de sabitlenmiştir.

## 4. Validation sonuçları

| Pencere | Validation aralığı | v1 net | v2 net | Benchmark net | v2 max DD | v1 / v2 trade |
|---:|---|---:|---:|---:|---:|---:|
| 0 | 2024-06-29–2024-07-29 | `-%6,04` | `-%0,71` | `%1,22` | `%1,32` | 114 / 27 |
| 1 | 2024-07-29–2024-08-28 | `-%6,95` | `-%1,50` | `-%1,35` | `%1,71` | 112 / 26 |
| 2 | 2024-08-28–2024-09-27 | `-%7,23` | `-%1,06` | `%0,90` | `%1,29` | 124 / 26 |
| 3 | 2024-09-27–2024-10-27 | `-%7,35` | `-%1,40` | `%0,23` | `%1,51` | 120 / 23 |
| 4 | 2024-10-27–2024-11-26 | `-%7,90` | `-%0,83` | `%3,78` | `%1,91` | 170 / 44 |

Birleşik özet:

- Tamamlanan trade: v1 `640`, v2 `146`; azalma `%77,19`.
- Toplam tahmini execution maliyeti: v1 `370,14 USDT`, v2 `87,39 USDT`; azalma `%76,39`.
- v2 bileşik net getiri: `-%5,38`.
- Benchmark bileşik net getiri: `%4,82`; v2 benchmark excess: `-%10,20`.
- v2 en kötü maksimum drawdown: `%1,91`.
- v2 kârlı pencere oranı: `%0` (`0/5`).

## 5. Önceden kayıtlı kabul kapısı

| Ölçüt | Eşik | Sonuç | Karar |
|---|---:|---:|---|
| Trade azalması | `≥ %30` | `%77,19` | Geçti |
| Execution maliyeti azalması | `≥ %30` | `%76,39` | Geçti |
| Bileşik net getiri | `> %0` | `-%5,38` | **Kaldı** |
| Benchmark excess | `≥ %0` | `-%10,20` | **Kaldı** |
| En kötü maksimum drawdown | `≤ %5` | `%1,91` | Geçti |
| Kârlı pencere oranı | `≥ %60` | `%0` | **Kaldı** |

Tüm koşulların birlikte sağlanması gerektiği için v2 reddedilmiştir. Başarısız eşikler validation sonucuna göre gevşetilmeyecektir.

## 6. Karar ve sonraki adım

- v2 hiçbir paper/testnet/live profile'a terfi ettirilmez.
- Yeni final OOS dönemi açılmaz veya indirilmez; gereksiz OOS tüketimi engellenir.
- 2024 validation ve 2025 v1 OOS verileri yeni parametre ayarı veya başarı iddiası için tekrar kullanılmaz.
- Sonraki strateji hipotezi yeni bir sürüm ve ADR ile önceden tanımlanmalı; ayrı geliştirme verisinde aynı fail-closed kapıdan geçmelidir.
- Öncelikli araştırma konusu, yalnız turnover azaltmak yerine pozitif expectancy üreten rejim/entry-edge filtresidir. Tick/lot rounding ve order-book queue replay ayrıca execution gerçekçiliği işi olarak kalır.

Bu sonuç aylık `%10` stretch hedefini düşürmez; hedef kabul matematiğini değiştirmek veya riski artırmak için kullanılmaz.
