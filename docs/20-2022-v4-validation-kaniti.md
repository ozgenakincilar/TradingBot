# 2022 v4 ADX rejim validation ret kanıtı

**Durum:** Reddedildi

**Tarih:** 2026-07-27

**Veri sınırı:** Yalnız train/validation; ayrılmış holdout açılmadı; forward OOS değildir

## Amaç

Bu çalışma, [v4 ön kaydında](19-v4-adx-rejim-on-kaydi.md) sonuç görülmeden kilitlenen yalnız-entry `1H ADX(14) >= 25` trend-kalite filtresini v2 ile karşılaştırır. 2023/2024/2025 sonuçları hipotezi etkilediği için daha önce indirilmemiş 2022 BTC-USDT verisi yalnız historical development robustness amacıyla kullanılmıştır.

## Dataset kimliği

| Timeframe | Source | Candle | SHA-256 |
|---|---|---:|---|
| `15m` | `okx-btc-usdt-15m-2022` | 35.040 | `EACA341A35D9FD9DBE423DB8643A455AB0A3B186651FF3C6283B6EFF462D0430` |
| `1H` | `okx-btc-usdt-1h-2022` | 8.760 | `F6FF0BCE792E0D0A576EDE99B61A3234E50BE168426B8489065DA37C48A64A71` |

Canonical CSV dosyaları public OKX export pipeline'ıyla üretildi ve Git dışında `data/` altında tutuldu. Küçük hacimlerin `G29` ile exponent biçiminde yazılabilmesi nedeniyle bulunan writer-reader sözleşme hatası [PR #28](https://github.com/ozgenakincilar/TradingBot/pull/28) ile testli olarak giderildi; dataset içeriği elle değiştirilmedi.

## Tekrar üretim

```powershell
dotnet run --project src/TradingBot.Research --configuration Release -- `
  validate-adx-regime-v4 `
  --instrument BTC-USDT `
  --signal data/btc-usdt-15m-2022.csv `
  --signal-source okx-btc-usdt-15m-2022 `
  --trend data/btc-usdt-1h-2022.csv `
  --trend-source okx-btc-usdt-1h-2022 `
  --from 2022-01-01T00:00:00.0000000+00:00 `
  --to 2023-01-01T00:00:00.0000000+00:00 `
  --training-days 180 `
  --validation-days 30 `
  --oos-days 30 `
  --mode rolling `
  --seed 20220727
```

- Şema: `adx-regime-validation-v1`
- Run SHA-256: `42B6F28067D617A161913555FCF04B233E53F44BC8C4968C9DBF2751BDF86FD5`
- Report SHA-256: `99CEDE56B42E16DD4A67A826968D16606EE1EA4A2F8B7B3F88EAD6D1707F8C06`
- Beklenen process exit code: `3`

## Toplu sonuç

| Ölçüm | v2 | v4 | Ön kayıt kapısı | Sonuç |
|---|---:|---:|---:|---|
| Tamamlanan trade | 126 | 72 | en az `%20` azalma ve en az 30 v4 trade | **Başarılı:** `%42,8571` azalma, 72 trade |
| Execution maliyeti | `75,0772` | `43,0684` | en az `%20` azalma | **Başarılı:** `%42,6345` azalma |
| Aggregate profit factor | — | `0,2355` | en az `1,10` | Başarısız |
| Compounded net return | — | `-%5,6275` | pozitif | Başarısız |
| Buy-and-hold compounded | — | `-%1,7839` | — | — |
| Benchmark excess | — | `-%3,8436` | negatif olmamalı | Başarısız |
| Worst drawdown | — | `%2,9691` | en fazla `%5` | **Başarılı** |
| Kârlı pencere | — | `%0` | en az `%60` | Başarısız |

v4 brüt kazanan trade toplamı `17,3119`, brüt kaybeden trade toplamı `73,4979` olmuştur. Beş validation penceresinin net getirileri sırasıyla `-%2,4963`, `-%1,1508`, `-%0,6730`, `-%1,3685` ve `-%0,0532` değerindedir.

## Karar

Sekiz kapının trade azalması, maliyet azalması, minimum aktivite ve drawdown olmak üzere yalnız dördü geçti. ADX filtresi turnover ve maliyet problemini anlamlı biçimde azalttı fakat seçtiği işlemler pozitif expectancy üretmedi; v4 reddedildi.

ADX periyodu/eşiği ve acceptance değerleri değiştirilmez. 2022 holdout açılmaz, sonuç forward OOS sayılmaz ve v4 paper/testnet/live profile'a alınmaz. Bir sonraki aday, aynı yıllarda parametre taraması yapmak yerine zararların giriş/exit ve rejim koşullarını yeni bir hipotez için analiz etmeli; yeni model ve veri sınırı sonuçtan önce tekrar kaydedilmelidir.
