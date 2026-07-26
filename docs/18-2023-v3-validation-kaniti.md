# 2023 v3 validation ret kanıtı

**Durum:** Reddedildi

**Tarih:** 2026-07-26

**Veri sınırı:** Yalnız train/validation; ayrılmış OOS açılmadı

## Amaç

Bu çalışma, [v3 ön kaydında](17-v3-on-kayit.md) sonuç görülmeden kilitlenen dört-candle re-entry cooldown ve 100/50 bps trailing profit-protection hipotezini v2 ile karşılaştırır. Hipotezi doğuran 2024 verisi yerine daha önce strateji seçimi için kullanılmamış 2023 BTC-USDT development verisi kullanılmıştır.

## Dataset kimliği

| Timeframe | Source | Candle | SHA-256 |
|---|---|---:|---|
| `15m` | `okx-btc-usdt-15m-2023` | 35.040 | `677A3BC37B26610F9599836199CCD1AAE437886C1F5F61C66C270C5E4227C508` |
| `1H` | `okx-btc-usdt-1h-2023` | 8.760 | `C1B9B3342E737F683AFF7E6F7DB61DBC98520E23508193AFD323C62DC89A95C2` |

Canonical CSV dosyaları atomik public OKX export pipeline'ıyla üretildi ve `data/` altında Git dışında tutuldu.

## Tekrar üretim

```powershell
dotnet run --project src/TradingBot.Research --configuration Release -- `
  validate-profit-protection-v3 `
  --instrument BTC-USDT `
  --signal data/btc-usdt-15m-2023.csv `
  --signal-source okx-btc-usdt-15m-2023 `
  --trend data/btc-usdt-1h-2023.csv `
  --trend-source okx-btc-usdt-1h-2023 `
  --from 2023-01-01T00:00:00.0000000+00:00 `
  --to 2024-01-01T00:00:00.0000000+00:00 `
  --training-days 180 `
  --validation-days 30 `
  --oos-days 30 `
  --mode rolling `
  --seed 20230726
```

- Şema: `profit-protection-validation-v1`
- Run SHA-256: `87B1A4D64A5579F0661BAB724D5B571AA1BA54A5030F4EB206D7FC7D037ACEF9`
- Report SHA-256: `02F3D5D1289AFCA7A4E37EED9CE9001543D438D074AD6D7831234F9DE9A51B16`
- Beklenen process exit code: `3`

## Toplu sonuç

| Ölçüm | v2 | v3 | Ön kayıt kapısı | Sonuç |
|---|---:|---:|---:|---|
| Tamamlanan trade | 106 | 124 | en az `%20` azalma | Başarısız: `%16,9811` artış |
| Execution maliyeti | `63,5278` | `73,9609` | en az `%20` azalma | Başarısız: `%16,4229` artış |
| Kârı geri verme oranı | `%45,2830` | `%41,1290` | en az `%30` azalma | Başarısız: yalnız `%9,1734` azalma |
| Compounded net return | — | `-%8,6300` | pozitif | Başarısız |
| Buy-and-hold compounded | — | `%2,1916` | — | — |
| Benchmark excess | — | `-%10,8215` | negatif olmamalı | Başarısız |
| Worst drawdown | — | `%3,5497` | en fazla `%5` | **Başarılı** |
| Kârlı pencere | — | `%0` | en az `%60` | Başarısız |

Beş validation penceresinin v3 net getirileri sırasıyla `-%1,1086`, `-%0,3567`, `-%1,4352`, `-%2,6110` ve `-%3,4026` olmuştur. Hiçbir pencere pozitif kapanmamıştır.

## Exit attribution

| Exit reason | Trade | Kazanan | Net PnL | Tahmini maliyet | Kârı geri veren |
|---|---:|---:|---:|---:|---:|
| `profit-protection-exit` | 39 | 29 | `+20,2135` | `23,2555` | 10 |
| `signal-ema-hysteresis-cross-down` | 76 | 0 | `-99,2932` | `44,7785` | 39 |
| `trend-filter-exit` | 9 | 0 | `-8,9775` | `5,3457` | 2 |

Profit-protection çıkışlarının kendi alt kümesinde olumlu sonuç üretmesi yeterli olmamıştır. Erken flat'e geçişten sonra yeniden oluşan EMA entry sinyalleri, dört-candle cooldown'a rağmen yeni round-trip döngüleri yaratmış; toplam trade ve maliyet v2'nin üzerine çıkmıştır. En büyük zarar hâlâ `signal-ema-hysteresis-cross-down` ile kapanan ve bu veride `0/76` kazanan üreten işlemlerdedir.

## Karar

Yedi kapının yalnız drawdown kapısı geçtiği için v3 reddedilmiştir. Cooldown, activation veya trailing değerleri bu sonuçtan sonra değiştirilmez. 2023 ayrılmış OOS açılmaz; v3 paper/testnet/live profile'a alınmaz.

Sonraki hipotez exit eşiğini tekrar taramak yerine, giriş kalitesini ve piyasa rejimini önceden tanımlayan bağımsız bir filtreyi hedeflemelidir. Yeni hipotez, yeni parametreler ve ayrı veri sınırı sonuç görülmeden önce tekrar ön kayda alınmalıdır.
