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

## Güvenlik

API anahtarları kaynak koda, `appsettings*.json` dosyalarına veya loglara yazılmaz. İlk sürüm yalnızca `Paper` modunda çalışır. Live moda geçiş ayrı bir güvenlik kontrol listesi ve açık onay gerektirir.

SQL Server connection string environment variable veya secret provider üzerinden verilir; repository dosyalarına yazılmaz. EF Core komutlarında `TRADINGBOT_DB_CONNECTION` environment variable kullanılır.
