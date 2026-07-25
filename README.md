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

## Güvenlik

API anahtarları kaynak koda, `appsettings*.json` dosyalarına veya loglara yazılmaz. İlk sürüm yalnızca `Paper` modunda çalışır. Live moda geçiş ayrı bir güvenlik kontrol listesi ve açık onay gerektirir.

SQL Server connection string environment variable veya secret provider üzerinden verilir; repository dosyalarına yazılmaz. EF Core komutlarında `TRADINGBOT_DB_CONNECTION` environment variable kullanılır.
