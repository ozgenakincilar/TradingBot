# Yazılım Mimarisi

**Durum:** Kabul edildi

## 1. Karar özeti

Sistem **modüler monolit** olarak dağıtılacak, **Clean Architecture** bağımlılık yönünü uygulayacak ve karmaşık iş kuralları için **taktiksel DDD** kullanacaktır. Onion Architecture ayrıca uygulanacak ayrı bir stil değildir; Clean Architecture ile ortak olan içe doğru bağımlılık ilkesi zaten korunur. SOA/mikroservis, yalnızca ölçülmüş bağımsız ölçekleme veya ekip sınırı ihtiyacı oluşursa değerlendirilecektir.

## 2. Bağımlılık kuralı

```text
Host -------------> Application <------------- Infrastructure
                         |
                         v
                       Domain
```

- `Domain`: Başka proje referansı yoktur.
- `Application`: Yalnızca Domain’e referans verir; portları tanımlar.
- `Infrastructure`: Application portlarını uygular; Domain’i kullanabilir.
- `Host`: Composition root’tur; DI, config, endpoint ve worker yaşam döngüsünü yönetir.
- Domain, Application veya Infrastructure hiçbir zaman Host’a referans vermez.

## 3. Hedef modüller

| Modül | Sorumluluk | Sahip olduğu veri |
|---|---|---|
| Market Data | Stream, snapshot, candle, order book ve veri kalitesi | Ham/normalize piyasa olayları |
| Strategy | Özellikler, indikatörler ve trade intent üretimi | Strateji durumu/sürümü |
| Risk | Emir öncesi ve portföy limitleri | Limitler, exposure snapshot |
| Execution | Emir state machine, idempotency ve reconciliation | Emirler ve executions |
| Portfolio | Bakiye, pozisyon, PnL ve exposure | Pozisyon snapshot’ları |
| Backtesting | Tarihsel replay ve fill modeli | Run ve sonuçlar |
| Operations | Kill switch, health, alert ve audit | Operasyon olayları |

İlk aşamada bu modüller aynı process ve veritabanında yaşar; tablo/şema ve namespace sınırları korunur.

## 4. Katman sorumlulukları

### Domain

- Aggregate, entity, value object ve domain service.
- Finansal invariants ve state transition kuralları.
- Domain event tanımları.
- Ağ, dosya, veritabanı, log veya DI bağımlılığı içermez.

### Application

- Use case orchestration ve transaction sınırları.
- Command/query ve port arayüzleri.
- Authorization ve idempotency akışı.
- DTO mapping; iş invariant’ı barındırmaz.

### Infrastructure

- Exchange REST/WebSocket adaptörleri.
- Persistence, secret provider, clock sync, notification.
- Retry, circuit breaker ve serialization ayrıntıları.
- Dış modelleri anti-corruption layer ile iç modele dönüştürür.

### Host

- Composition root ve process lifecycle.
- Worker/API endpoint’leri.
- Options validation, health checks ve telemetry wiring.
- Ortam seçimi; secret değerleri okumadan sadece sağlayıcıları bağlar.
- `TradingWorker` singleton yaşam döngüsündedir fakat her market-event turunda yeni async DI scope açar; scoped `DbContext`, repository veya application handler saklamaz.

## 5. İletişim modeli

- Process içi komutlar doğrudan application handler çağrısıdır.
- Yüksek hacimli market data bounded `Channel<T>` üzerinden akar.
- Bounded channel `FullMode=Wait` kullanır; kapasite dolduğunda producer'a backpressure uygular ve kritik market event'i sessizce düşürmez.
- Başlangıç/reconnect sırasında WebSocket event'leri buffer'da tutulur; REST snapshot sequence'inden eski overlap atılır ve kalan seri tamamen doğrulanmadan aşağı akışa yayınlanmaz.
- `MarketSnapshotService`, instrument başına integrity guard'ı process ömründe tutar; aynı instrument değerlendirmelerini `SemaphoreSlim` ile sıralar ve yalnız fresh/ready event'i execution hattına verir.
- Domain event aynı transaction içindeki yan etkileri ayırır.
- Integration event ancak dış servis veya gelecekte ayrılacak modül sınırında kullanılır.
- Kuyruk dolduğunda veri türüne göre açık backpressure politikası uygulanır; kritik execution olayı sessizce düşürülemez.

## 6. Veri tutarlılığı

- SQL Server içindeki tek iş kararına ait state, audit ve outbox değişiklikleri kısa ACID transaction içinde atomik kaydedilir.
- Borsa HTTP/WebSocket çağrısı SQL transaction içine alınmaz; çağrıdan önce ve sonra ayrı kısa transaction kullanılır.
- Execution aggregate üzerinde optimistic concurrency kullanılır.
- Dış emir çağrıları idempotent client order ID ile yapılır.
- “Gönderildi mi?” sonucu belirsizse yeni emir gönderilmez; önce borsadan sorgulanır.
- Kalıcı durum ve integration event aynı transaction'da Transactional Outbox ile yazılır.
- Exchange gerçeğin kaynağıdır; yerel projection periyodik reconciliation ile düzeltilir.

### CAP ve ağ bölünmesi

- Execution, Risk ve Portfolio için ağ bölünmesinde tutarlılık kullanılabilirliğe tercih edilir.
- Exchange state, market-data sequence veya zaman geçerliliği kesin değilse yeni exposure durdurulur.
- Telemetry, dashboard ve analitik projection'larda sınırlı eventual consistency kabul edilir.
- Ayrıntılı ve bağlayıcı karar [ADR-0005](adr/0005-acid-cap-ve-tutarlilik.md) içindedir.

## 7. Hata modeli

Hatalar dört sınıfa ayrılır:

1. Validation/business rejection: retry edilmez.
2. Transient dependency failure: sınırlı retry + jitter.
3. Unknown execution outcome: reconciliation gerekir, kör retry yasaktır.
4. Invariant/security breach: ilgili işlem hattı durdurulur ve kritik alarm üretilir.

## 8. Evrim stratejisi

Bir modül ancak aşağıdaki koşullardan biri ölçülebilir şekilde oluşursa ayrı servise çıkarılır:

- Farklı ölçekleme profili.
- Ayrı güvenlik veya hata izolasyonu sınırı.
- Bağımsız deployment gerektiren ekip sahipliği.
- Tek process SLO hedefini karşılayamıyor.

Ayrıştırma öncesinde modülün portları, veri sahipliği ve integration event sözleşmeleri hazır olmalıdır.
