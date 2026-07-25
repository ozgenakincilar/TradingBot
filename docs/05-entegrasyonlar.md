# Entegrasyon Tasarımı

**Durum:** Taslak

## 1. Portlar

Application katmanında borsadan bağımsız aşağıdaki portlar tanımlanacaktır:

- `IMarketDataStream`
- `IMarketSnapshotClient`
- `ISpotInstrumentCatalog`
- `IClosedCandleHistoryClient`
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

1. REST instrument catalog ile sembol, Spot türü, `live` durumu ve tick/lot/minimum quantity filtreleri doğrulanır.
2. Enstrüman kapısı geçerse WebSocket stream başlatılır ve buffer edilir.
3. REST başlangıç snapshot'ı alınır.
4. Snapshot sequence ile buffer hizalanır.
5. İlk doğrulanmış event sonrası market-data readiness açılır.
6. Gap/stale/kopma durumunda ilgili sembol “not ready” yapılır.
7. REST snapshot ile onarım tamamlanınca yayın yeniden açılır.

Sequence/timestamp invariant'ları borsa DTO'sundan bağımsız `MarketDataIntegrityGuard` içinde uygulanır. Adaptör, borsanın update ID/sequence değerini ve kararlı event ID'yi `MarketDataCursor` sözleşmesine dönüştürür; çelişki veya gap sonrası doğrudan ready açamaz.

Mevcut application portu `IMarketDataClient.GetTopOfBookAsync` ile normal olayı, `GetRecoverySnapshotAsync` ile authoritative snapshot'ı ayırır. `MarketSnapshotService` ilk event'te ve her gap/conflict sonrasında recovery çağırır; duplicate, out-of-order veya stale sonuç `TradingWorker` tarafından execution cycle'a geçirilmez.

`MarketDataEventBuffer` bounded ve wait-backpressure politikalıdır. `MarketDataReplayAligner`, snapshot sequence'ine eşit/eski overlap'i atar, daha yeni event'leri geliş sırasıyla doğrular ve tüm seri contiguous değilse boş sonuç döndürür. Böylece replay'in doğrulanmış ilk kısmı bile yanlışlıkla strategy/execution hattına sızmaz.

`OkxSpotMarketStreamClient`, OKX public `books5` WebSocket snapshot kanalını `IMarketDataStreamClient` portuna dönüştürür. Subscribe acknowledgement market event sayılmaz; API error serbest metni sanitize edilir. Gerçek endpoint connectivity testi opt-in environment flag ile çalışır.

`MarketDataStreamSession` WebSocket producer'ını önce başlatır ve event'leri bounded buffer'a alırken REST snapshot ister. OKX `books5` tam-snapshot modunda REST sonucu freshness/cross-source kontrolüdür; ilk WebSocket snapshot'ı sequence anchor'ı olur ve sonraki tam snapshot'lar timestamp/sequence geriye sarma korumasıyla uygulanır. `OkxTradingWorker` validated stream'i paper execution cycle'a taşır; kopmada 1–16 saniye üstel backoff üzerine 100–1000 ms jitter uygular. Worker singleton state içinde `DbContext` tutmaz; her ekonomik event için ayrı async scope açar.

`OkxSpotInstrumentCatalog`, public instruments endpoint'ini `ISpotInstrumentCatalog` portuna dönüştürür. `OkxInstrumentStartupGate` hosted worker sıralamasında stream supervisor'dan önce çalışır; sembol/tür/filtre/state uyumsuzluğunda host başlangıcını durdurur. Aynı kapı, yapılandırılmış timeframe/lookback için kapalı candle warm-up'ını tamamlar. Ortak `TradingReadinessState`, instrument, candle-history ve ilk geçerli market event kapılarını ayrı izler; üçünün tamamı geçmeden OKX readiness açılmaz ve stream kesilince market-data readiness kapanır.

`IClosedCandleHistoryClient`, kapalı candle geçmişini borsa DTO'sundan ayırır. `RecoverClosedCandleGap` beklenen ilk sınırdan gözlenen son kapalı sınıra kadar bounded REST aralığı ister; yanıt eksiksiz ve contiguous değilse kısmi seri döndürmez. `OkxClosedCandleHistoryClient`, V5 history-candles yanıtını ters kronolojik sıradan normalize eder, `confirm=1` zorunluluğunu ve UTC timeframe allowlist'ini uygular. [Resmi OKX V5 API](https://www.okx.com/docs-v5/en/#order-book-trading-market-data-get-candlesticks-history)

`WarmUpClosedCandles`, stratejiden bağımsız gerekli lookback sayısını alır. Borsa portundan yalnız tamamlanmış aralığı ister ve `ClosedCandleSequenceGuard` recovery kapısı geçmeden sonucu aşağı akışa açmaz. Host şu anda config'teki `15m/200` signal serisini doğrular; onaylanan strateji zarfının `1H/200` trend serisi için ikinci bağımsız warm-up/readiness bağlantısı henüz eklenmemiştir.

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

- İlk Spot borsası: OKX TR olarak seçildi; [ADR-0008](adr/0008-okx-tr-spot-ilk-borsa.md).
- Secret vault.
- Birincil ve yedek bildirim kanalı.
- Metrik/trace backend’i.
