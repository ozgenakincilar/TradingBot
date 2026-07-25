# Teknik Gereksinimler

**Durum:** Kabul edildi

## 1. Platform

- .NET SDK ve target framework: `net10.0`.
- C# nullable reference types açık.
- Release build’de warning’ler error kabul edilir.
- Production hedefi Linux x64 container/process.
- Tüm zamanlar UTC; iş mantığında local timezone kullanılmaz.
- Finansal değerlerde `decimal`; ölçüm/indikatör gerekli görürse sınırları doğrulanmış `double`.

## 2. Kod kuralları

- Async I/O uçtan uca `CancellationToken` alır.
- `.Result`, `.Wait()`, `async void` ve izlenmeyen fire-and-forget yasaktır.
- Hot path üzerinde kontrolsüz allocation ve string birleştirme yapılmaz.
- Public API’ler XML doc veya anlaşılır sözleşmeye sahip olur.
- Domain primitive obsession’dan kaçınır; value object kullanır.
- Dış API DTO’ları Domain’e sızmaz.
- Static global mutable state kullanılmaz.
- `DateTimeOffset.UtcNow` yerine test edilebilir `TimeProvider` enjekte edilir.
- Rastgelelik ve ID üretimi testte değiştirilebilir portlarla sağlanır.

## 3. Eşzamanlılık

- Paylaşılan mutable state en aza indirilir.
- Stream işleme bounded `Channel<T>` kullanır.
- Kilit gerekiyorsa async uyumlu primitive ve kısa critical section kullanılır.
- Her sembol/emir için sıralama garantisi açıkça tanımlanır.
- Background task’lar host lifecycle tarafından sahiplenilir ve gözlemlenir.

## 4. Ağ ve HTTP

- `IHttpClientFactory` veya uzun ömürlü handler bağlantı havuzu kullanılır.
- Timeout her çağrı türünde açıkça belirlenir.
- Retry sadece idempotent ve transient işlemlerde uygulanır.
- Exponential backoff + full jitter zorunludur.
- Rate-limit weight ve response header’ları izlenir.
- WebSocket heartbeat, stale stream, sequence gap ve reconnect yönetilir.
- TLS sertifika doğrulaması kapatılamaz.
- DNS/connection lifetime ölçülerek yapılandırılır; sabit IP’ye kör pinleme yapılmaz.

## 5. Yapılandırma

Öncelik sırası:

1. Kod içindeki güvenli varsayılanlar.
2. `appsettings.json` (secret içermez).
3. Ortama özel config (repoya production secret girmez).
4. Environment variables.
5. Secret provider.

Options sınıfları başlangıçta `ValidateOnStart` ile doğrulanır. Geçersiz veya riskli yapılandırma fail-fast davranır.

## 6. Veri saklama

Ana ilişkisel veritabanı **Microsoft SQL Server**'dır. Veri erişimi Infrastructure katmanında EF Core SQL Server provider üzerinden sağlanacaktır.

- UTC timestamp ve yüksek çözünürlüklü exchange event time saklanır.
- Emir/fill/audit kayıtları append ağırlıklıdır.
- Para ve miktar kolonlarında `decimal` precision/scale açıkça tanımlanır; `money`/`smallmoney` kullanılmaz.
- Optimistic concurrency için `rowversion` kullanılır.
- Migration’lar sürümlenir ve geri dönüş planı içerir.
- Mum/tick tabloları UTC zaman ve instrument üzerinden uygun clustered/nonclustered index'lere sahip olur.
- Tick verisi retention, batch/bulk insert ve gerektiğinde SQL Server partitioning politikasına tabidir.
- Execution transaction'ları ile yüksek hacimli market-data yazımları ayrı şema, repository ve iş yükü sınırlarında tutulur.
- Backup restore düzenli olarak test edilir.
- `portfolio.AssetBalances` ve `portfolio.SpotPositions` güncel aggregate snapshot'larını `rowversion` ile saklar.
- `portfolio.SpotExecutions`, `(Exchange, ExchangeExecutionId)` birleşik anahtarıyla duplicate fill'i veritabanı seviyesinde engeller.
- Portfolio snapshot, execution ledger, audit ve outbox aynı Serializable transaction'da yazılır.
- `portfolio.SpotOrderReservations`, `execution.Orders` ile bire bir bağlıdır ve açık/partial emir fonlarını restart sonrasında yeniden kurmak için saklar.
- Order state, reservation, balance, position, execution ledger, audit ve outbox aynı fill/cancel kararında atomik güncellenir.
- Reconciliation run'ları snapshot hash ile idempotent saklanır; aynı snapshot ID farklı içerikle yeniden kullanılamaz.
- `operations.TradingSafetyStates`, reconciliation farkında `rowversion` korumalı halt durumunu taşır ve execution persistence yeni exposure'ı reddeder.
- Temiz reconciliation sonucu halt'ı otomatik temizleyemez; recovery için ayrıca yetkili, audit edilen bir operasyon gerekir.
- Safety recovery iki ardışık temiz snapshot, benzersiz recovery ID, operatör kimliği ve gerekçe olmadan çalışmaz.
- Recovery kanıtı `operations.TradingSafetyRecoveries` tablosunda append-only tutulur; safety state, recovery, audit ve outbox aynı transaction'da yazılır.
- Risk kararı son safety transition'dan eskiyse, halt kaldırılmış olsa bile execution persistence tarafından reddedilir.

### ACID transaction gereksinimleri

- Order state, audit ve outbox kaydı aynı iş kararına aitse tek ACID transaction içinde yazılır.
- Fill, order quantity ve portfolio değişikliği atomik uygulanır veya tamamen rollback edilir.
- Transaction süreleri kısa ve bounded tutulur; ağ, dosya veya bildirim I/O'su transaction içinde çalışmaz.
- Borsa çağrısı öncesi ve sonrası ayrı transaction kullanılır.
- Deadlock/serialization retry yalnızca tüm use case idempotent ise ve bounded policy ile yapılır.
- Aggregate concurrency conflict'i `rowversion` ile algılanır; lost update kabul edilmez.
- Outbox kaydı domain değişikliğiyle aynı transaction'da yazılır; gönderim ayrı worker tarafından en az bir kez yapılabilir.
- Outbox tüketicileri duplicate event'lere karşı idempotent olmak zorundadır.

### CAP ve tutarlılık gereksinimleri

- CAP tercihi yalnızca borsa, gelecekteki servisler veya replika gibi dağıtık sınırlar için geçerlidir.
- Execution/Risk/Portfolio ağ bölünmesinde CP eğilimlidir: tutarlılık kanıtlanamıyorsa işlem durur.
- `Unknown` order, stale market data, sequence gap veya reconciliation farkı yeni exposure'ı engeller.
- Dashboard, telemetry ve analitik read model eventual consistent olabilir ve trading kararı için kaynak olamaz.

## 7. Serileştirme ve sözleşmeler

- `System.Text.Json` varsayılandır.
- Event/DTO sözleşmeleri schema version taşır.
- Bilinmeyen enum ve eksik alan davranışı test edilir.
- Borsa payload’ları arşivlenecekse secret/header temizliği uygulanır.

## 8. Paket yönetimi

- Harici paket ancak platform özelliği yetersizse eklenir.
- Paket sürümleri merkezi ve sabitlenmiş yönetilmelidir.
- Vulnerability ve outdated taraması CI’da çalışır.
- Lisans uyumluluğu kontrol edilir.

## 9. Repository kalite kapıları

Her değişiklik için:

```powershell
dotnet format --verify-no-changes
dotnet build TradingBot.slnx --configuration Release
dotnet test TradingBot.slnx --configuration Release --no-build
dotnet list TradingBot.slnx package --vulnerable --include-transitive
```

Test projesi ve CI kurulana kadar eksik kapılar yol haritasında takip edilir.
