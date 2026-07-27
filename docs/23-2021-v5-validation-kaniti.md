# 2021 v5 DMI yön validation ret kanıtı

**Durum:** Reddedildi

**Tarih:** 2026-07-27

**Veri sınırı:** Yalnız train/validation; ayrılmış holdout açılmadı; forward OOS değildir

## Amaç

Bu çalışma, [v5 ön kaydında](22-v5-dmi-yon-on-kaydi.md) sonuç görülmeden kilitlenen entry-only `+DI > -DI` yön filtresini v4 ile karşılaştırır. 2021 BTC-USDT verisi ön kayıt ve implementasyon tamamlandıktan sonra public OKX export pipeline'ıyla indirildi. Sonuç yalnız historical development robustness kanıtıdır; final model seçimi veya canlı işlem izni değildir.

## Dataset kimliği

| Timeframe | Source | Candle | SHA-256 |
|---|---|---:|---|
| `15m` | `okx-btc-usdt-15m-2021` | 35.040 | `B157C5B1217316CECA93841F5D11073956BD281A70A9E2864D51F7D3408E6AA2` |
| `1H` | `okx-btc-usdt-1h-2021` | 8.760 | `47DEDC2A02AABA89D37129DF1C1CB008DEF9FD6DEA447DFC91A67E50C5B4DF00` |

Canonical CSV dosyaları Git dışında `data/` altında tutulur. Her iki seri de tam `2021-01-01T00:00:00Z`–`2022-01-01T00:00:00Z` aralığını kapsar.

## Tekrar üretim

```powershell
dotnet run --project src/TradingBot.Research --configuration Release -- `
  validate-dmi-direction-v5 `
  --instrument BTC-USDT `
  --signal data/btc-usdt-15m-2021.csv `
  --signal-source okx-btc-usdt-15m-2021 `
  --trend data/btc-usdt-1h-2021.csv `
  --trend-source okx-btc-usdt-1h-2021 `
  --from 2021-01-01T00:00:00.0000000+00:00 `
  --to 2022-01-01T00:00:00.0000000+00:00 `
  --training-days 180 `
  --validation-days 30 `
  --oos-days 30 `
  --mode rolling `
  --seed 20210727
```

- Şema: `dmi-direction-validation-v1`
- Run SHA-256: `F9315563CB997F9D9E596DC5888D87DA3EC52A13C4A8E3663E398A10F4906D9F`
- Report SHA-256: `45922A3E2DD8D0C60EA5E6BAFEBDBF8386A1284EC941D9F2E3F385A57148EB18`
- Beklenen process exit code: `3`

## Toplu sonuç

| Ölçüm | v4 | v5 | Ön kayıt kapısı | Sonuç |
|---|---:|---:|---:|---|
| Tamamlanan trade | 113 | 80 | v5 en az 30 ve v4'ten az | **Başarılı:** `%29,2035` azalma, 80 trade |
| Execution maliyeti | `66,9637` | `47,6139` | v4'ten düşük | **Başarılı:** `%28,8959` azalma |
| Aggregate profit factor | `0,3293` | `0,4170` | v5 en az `1,10` ve v4'ten yüksek | Başarısız; v4'ten yüksek fakat mutlak eşik altında |
| Compounded net return | — | `-%4,5272` | pozitif | Başarısız |
| Buy-and-hold compounded | — | `%4,7063` | — | — |
| Benchmark excess | — | `-%9,2336` | negatif olmamalı | Başarısız |
| Worst drawdown | — | `%2,1772` | en fazla `%5` | **Başarılı** |
| Kârlı pencere | — | `%0` | en az `%60` | Başarısız |

Beş validation penceresinin v5 net getirileri sırasıyla `-%0,8387`, `-%2,1763`, `-%0,7261`, `-%0,4991` ve `-%0,3605` değerindedir. Yön filtresi profit factor'ı v4'e göre yaklaşık `%26,6444` yükseltmiştir; buna rağmen tek bir pencere dahi pozitif kapanmamıştır.

## Karar

Sekiz kapının minimum aktivite, trade azalması, maliyet azalması ve drawdown olmak üzere yalnız dördü geçti. Strict DMI yön koşulu turnover ve execution maliyetini azalttı, göreli profit factor'ı iyileştirdi; fakat pozitif expectancy üretmedi, buy-and-hold benchmark'ını geçemedi ve v5 reddedildi.

ADX/DI parametreleri veya acceptance kapıları sonuçtan sonra değiştirilmez. 2021 holdout açılmaz, sonuç forward OOS sayılmaz ve v5 paper/testnet/live profile'a alınmaz. Aynı geçmiş yıllarda yeni eşik taraması yapılmayacaktır. Sonraki araştırma hipotezi ayrı ön kayıt ve veri sınırı gerektirir; final kabul için 2026-07-27 sonrasında oluşan gerçek forward OOS veri zorunludur.
