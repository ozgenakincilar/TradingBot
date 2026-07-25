# Entegrasyon Tasarımı

**Durum:** Taslak

## 1. Portlar

Application katmanında borsadan bağımsız aşağıdaki portlar tanımlanacaktır:

- `IMarketDataStream`
- `IMarketSnapshotClient`
- `IInstrumentCatalog`
- `IOrderGateway`
- `IAccountGateway`
- `IExchangeClock`
- `IOrderRepository`
- `IUnitOfWork`
- `ISecretProvider`
- `INotificationPublisher`
- `IKillSwitch`

Her adaptör exchange DTO’sunu kendi assembly/namespace sınırında tutar.

Borsa adaptörü yalnızca Spot market-data, account ve order endpoint'lerini uygular. Futures/margin endpoint, credential izni veya instrument türü algılanırsa adaptör trading-ready olamaz.

## 2. Market data başlangıç akışı

1. REST ile instrument filtreleri ve başlangıç snapshot alınır.
2. WebSocket stream başlatılır ve buffer edilir.
3. Snapshot sequence ile buffer hizalanır.
4. Sıralı event’ler normalize edilerek yayınlanır.
5. Gap/stale durumunda ilgili sembol “not ready” yapılır.
6. REST snapshot ile onarım tamamlanınca yayın yeniden açılır.

## 3. Emir gönderimi

- Risk kararı ve intent aynı correlation içinde tutulur.
- Client order ID borsa sınırlarına uygun ve deterministik/idempotent üretilir.
- Order, audit ve outbox ilk kısa ACID transaction ile `Submitting` olarak kaydedilir; transaction commit edilmeden borsaya çağrı yapılmaz.
- Borsa çağrısı boyunca database transaction açık tutulmaz.
- Borsa sonucu ikinci kısa transaction ile kalıcılaştırılır.
- Submit timeout’u “başarısız” kabul edilmez; `Unknown` durumudur.
- Unknown durumda önce client order ID ile query yapılır.
- Cancel/fill yarışı state machine tarafından çözülür.
- Borsa destekliyorsa amend tercih edilir; değilse cancel-replace ayrı idempotency ile yapılır.

### Network partition davranışı

- Exchange'e ulaşılamıyorsa yeni order submission durdurulur.
- Mevcut server-side koruyucu emirler yerel bağlantıdan bağımsız çalışmaya devam eder.
- Emir sonucu belirsizse sembol/hesap için risk artıran komutlar bloke edilir.
- Bağlantı döndüğünde account, order ve trade history reconcile edilir.
- Reconciliation başarılı olmadan readiness ve otomatik trading açılmaz.
- İlk reconciliation dilimi account `canTrade`, Spot balance ve bot-scoped aktif order snapshot'larını karşılaştırır.
- Farklar otomatik olarak yerel finansal state'in üzerine yazılmaz; run/audit/outbox ile kaydedilip kalıcı trading halt etkinleştirilir.
- Aynı snapshot ID yalnız aynı canonical SHA-256 içerikle idempotent kabul edilir.
- Halt kaldırma, ardışık temiz snapshot ve operatör onayı politikası uygulanana kadar otomatik değildir.
- Kontrollü recovery iki ardışık temiz snapshot ister; recovery ID, operatör ve gerekçe audit/outbox ile kalıcılaştırılır.
- Recovery, halt sırasında üretilmiş eski intent/risk kararlarını yeniden etkinleştirmez; yeni risk değerlendirmesi gerekir.

## 4. Dayanıklılık matrisi

| İşlem | Retry | Not |
|---|---|---|
| Market snapshot GET | Evet, sınırlı | Jitter ve rate limit uyumlu |
| Instrument metadata GET | Evet | Cache + geçerlilik süresi |
| Order query GET | Evet | Reconciliation için güvenli |
| Order POST | Kör retry yok | ClientOrderId ile önce sorgula |
| Cancel/Amend | Koşullu | Son durum sorgulanmalı |
| WebSocket connect | Evet | Exponential backoff + jitter |
| Authentication failure | Hayır | Kritik alarm ve trading halt |
| Validation rejection | Hayır | Domain/config hatası |

## 5. Rate limit

- Endpoint weight merkezi limiter tarafından takip edilir.
- Kritik order/reconciliation çağrıları için kapasite ayrılır.
- 429/418 benzeri cevaplar borsa politikasına göre cooldown uygular.
- Retry storm oluşmaması için process genelinde koordinasyon yapılır.

## 6. Zaman senkronizasyonu

- Exchange server time düzenli ölçülür.
- Round-trip latency hesaba katılarak offset tahmin edilir.
- Offset güven eşiğini aşarsa signed request ve yeni emir durdurulur.
- OS seviyesinde NTP/chrony ayrıca zorunludur.

## 7. Adaptör kabul testleri

- Recorded payload parsing.
- Timestamp/sequence edge case’leri.
- Rate-limit header parsing.
- Partial fill ve out-of-order user stream.
- Timeout sonrası order lookup.
- Reconnect ve gap fill.
- Exchange bakım/hesap kilidi cevapları.

## 8. Seçim bekleyen entegrasyonlar

- İlk Spot borsası.
- Secret vault.
- Birincil ve yedek bildirim kanalı.
- Metrik/trace backend’i.
