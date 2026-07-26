# 2024 v2 işlem kaybı attribution kanıtı

**Durum:** Aday reddedilmiş olarak kalır

**Tarih:** 2026-07-26

**Veri sınırı:** Yalnız train/validation; ayrılmış OOS açılmadı

## Amaç

Bu çalışma, reddedilmiş 30 bps EMA hysteresis v2 adayının neden zarar ettiğini işlem bazında ölçer. Sonuç yeni parametre seçmek, acceptance eşiklerini gevşetmek veya canlı işlem izni vermek için kullanılmaz.

Her tamamlanan işlem için giriş/çıkış reason code'u, gerçekleşen ortalama fiyatlar, net PnL, fee/spread/slippage maliyetleri, maksimum olumlu fiyat hareketi (MFE), maksimum olumsuz fiyat hareketi (MAE) ve holding süresi kaydedilir. Çıkışın gerçekleştiği mumun sonradan oluşan high/low değeri excursion hesabına alınmaz.

## Tekrar üretim

```powershell
dotnet run --project src/TradingBot.Research --configuration Release -- `
  diagnose-hysteresis-v2 `
  --instrument BTC-USDT `
  --signal data/btc-usdt-15m-2024.csv `
  --signal-source okx-btc-usdt-15m-2024 `
  --trend data/btc-usdt-1h-2024.csv `
  --trend-source okx-btc-usdt-1h-2024 `
  --from 2024-01-01T00:00:00.0000000+00:00 `
  --to 2025-01-01T00:00:00.0000000+00:00 `
  --training-days 180 `
  --validation-days 30 `
  --oos-days 30 `
  --mode rolling `
  --seed 20240726
```

- Şema: `strategy-loss-diagnostics-v1`
- Run SHA-256: `C6C9D8A246BD4F92D05E2CE4E0771020F099A6C23B6005CFE752BBB027867B46`
- Report SHA-256: `AC9D6760DF4A6FB77524C94C80216C1F6DD5109A64743C9E9C0B23A12A35EBD3`
- Strateji: `btc-usdt-long-flat-baseline`, version `2`

## Bulgular

| Ölçüm | Sonuç |
|---|---:|
| Tamamlanan işlem | 146 |
| Zararlı işlem | 110 (`%75,34`) |
| Kazanan işlem | 36 (`%24,66`) |
| Toplam net işlem PnL | `-55,0364 USDT` |
| Tahmini fee + spread + slippage | `87,0932 USDT` |
| Maliyet öncesi tahmini PnL | `+32,0568 USDT` |
| Kâra geçip zararla/başa baş kapanan işlem | 78 (`%53,42`) |

| Exit reason | İşlem | Kazanan | Net PnL | Maliyet | Ortalama MFE | Ortalama MAE | Kârı geri veren |
|---|---:|---:|---:|---:|---:|---:|---:|
| `signal-ema-hysteresis-cross-down` | 138 | 36 | `-46,3567` | `82,3415` | `%1,2951` | `%0,9059` | 74 |
| `trend-filter-exit` | 8 | 0 | `-8,6797` | `4,7517` | `%0,1765` | `%0,8945` | 4 |

Beş validation penceresinin tamamı negatiftir. Pencere net getirileri sırasıyla `-%0,7145`, `-%1,4982`, `-%1,0619`, `-%1,3985` ve `-%0,8254`; win rate aralığı `%13,04–%37,04` olmuştur.

## Yorum ve karar

Ana sorun tek başına “hiç sinyal avantajı yok” değildir: kullanılan tahmini modele göre işlemler maliyet öncesinde toplam `+32,0568 USDT` üretmiştir. Ancak küçük brüt avantaj, `87,0932 USDT` iki yönlü execution maliyetini karşılamamaktadır. Ayrıca 78 işlemde olumlu hareketin geri verilmesi EMA tabanlı çıkışın kazancı yeterince korumadığını gösterir. Trend-filter çıkışlarının `0/8` kazanma oranı da rejim filtresinin dönüşü geç doğruladığını düşündüren araştırma kanıtıdır; nedensellik iddiası değildir.

Bu nedenle v2 reddedilmiş kalır. Sonraki aday aynı EMA parametrelerini optimize ederek üretilmeyecek; daha düşük turnover ve açık exit/risk hipotezi önce ayrı development verisinde ön kayıtlı kapılarla sınanacaktır. Aylık `%10` hedefi acceptance eşiklerini, kaldıraç politikasını veya risk limitlerini değiştirmez.
