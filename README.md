# TradingBot

.NET 10 ile geliştirilen, güvenli varsayılanlara sahip kripto para işlem botu.

> Durum: Mimari hazırlık aşaması. Canlı emir gönderimi devre dışıdır.

Ürün kapsamı yalnızca **kaldıraçsız Spot trading**'dir. Futures, margin, short ve borçlanma desteklenmez.

## Mimari yaklaşım

- Modüler monolit
- Clean Architecture bağımlılık kuralları
- Taktiksel Domain-Driven Design (DDD)
- Borsa entegrasyonlarında Ports & Adapters
- Gerektiğinde servislere ayrılabilecek modül sınırları

## Dokümantasyon

Başlangıç noktası: [Dokümantasyon İndeksi](docs/README.md)

Bağlayıcı güvenlik ve operasyon kuralları: [instructions.md](instructions.md)

## Proje yapısı

```text
src/
  TradingBot.Domain/
  TradingBot.Application/
  TradingBot.Infrastructure/
  TradingBot.Host/
  TradingBot.Research/
docs/
```

## Yerel doğrulama

```powershell
$env:ConnectionStrings__TradingBot = '<SQL Server connection string>'
dotnet restore TradingBot.slnx
dotnet build TradingBot.slnx --configuration Release
dotnet run --project src/TradingBot.Host
```

Sağlık kontrolü: `GET /health`

## Tarihsel araştırma datası

Public OKX candle geçmişini API anahtarı olmadan canonical CSV olarak dışa aktarmak için:

```powershell
New-Item -ItemType Directory -Force data
dotnet run --project src/TradingBot.Research --configuration Release -- `
  export-candles `
  --instrument BTC-USDT `
  --timeframe 15m `
  --from 2025-01-01T00:00:00.0000000+00:00 `
  --to 2025-02-01T00:00:00.0000000+00:00 `
  --source okx-btc-usdt-15m-2025-01 `
  --output data/btc-usdt-15m-2025-01.csv
```

Yalnız `15m` ve `1H`, exact UTC round-trip zamanları ve mevcut olmayan `.csv` hedefi kabul edilir. `data/` Git tarafından izlenmez. Komut overwrite yapmaz; başarıda artifact SHA-256 ve range özetini JSON olarak döndürür.

Gerçek canonical CSV çiftinde versioned baseline stratejiyi rolling veya expanding walk-forward olarak çalıştırmak için:

```powershell
dotnet run --project src/TradingBot.Research --configuration Release -- `
  run-walk-forward `
  --instrument BTC-USDT `
  --signal data/btc-usdt-15m-2025.csv `
  --signal-source okx-btc-usdt-15m-2025 `
  --trend data/btc-usdt-1h-2025.csv `
  --trend-source okx-btc-usdt-1h-2025 `
  --from 2025-01-01T00:00:00.0000000+00:00 `
  --to 2026-01-01T00:00:00.0000000+00:00 `
  --training-days 180 `
  --validation-days 30 `
  --oos-days 30 `
  --mode rolling `
  --seed 42
```

Komut yalnızca `BTC-USDT`, canonical `15m/1H` CSV çifti ve v1 strateji zarfını kullanır. Execution policy rapor manifestinde sabittir: `1.000 USDT`, `%10` allocation, `20 bps` sentetik spread, `%0,1` komisyon, `10 bps` slippage, `%5` önceki-candle likidite katılımı ve `100 ms` latency. Çıktı `walk-forward-report-v2` JSON raporudur; strateji ile maliyetli buy-and-hold benchmark karşılaştırmasını içerir.

v1 ile cost-derived `30 bps` hysteresis v2 adayını yalnız train/validation pencerelerinde karşılaştırmak için aynı argümanlarla `run-walk-forward` yerine `validate-hysteresis-v2` kullanılır. Komut OOS candle'larını strategy stream'ine vermez; `strategy-validation-report-v1` JSON üretir. Tüm önceden kayıtlı kapılar geçerse exit code `0`, aday reddedilirse exit code `3` döner. Ret kodu bir uygulama arızası değil, fail-closed araştırma sonucudur.

## Güvenlik

API anahtarları kaynak koda, `appsettings*.json` dosyalarına veya loglara yazılmaz. İlk sürüm yalnızca `Paper` modunda çalışır. Live moda geçiş ayrı bir güvenlik kontrol listesi ve açık onay gerektirir.

SQL Server connection string environment variable veya secret provider üzerinden verilir; repository dosyalarına yazılmaz. EF Core komutlarında `TRADINGBOT_DB_CONNECTION` environment variable kullanılır.
