# Instructions Uyumluluk Matrisi

**Durum:** Yaşayan belge  
**Kaynak:** [`instructions.md`](../instructions.md)  
**Son inceleme:** 2026-07-25

Bu matris, savunma anayasasındaki 100 kuralın unutulmamasını ve her iddianın kod, test, ADR veya runbook kanıtına bağlanmasını sağlar. Bir özelliğin planlanmış olması uygulanmış sayılmaz.

## Statü tanımları

| Statü | Anlamı |
|---|---|
| ✅ Uygulandı | Kod ve otomatik test veya operasyonel kanıt mevcut |
| 🟡 Kısmi | Temel kontrol mevcut; kuralın tüm kabul ölçütleri kapanmadı |
| ⬜ Planlandı | Mimari kapsamda fakat henüz uygulanmadı |
| ➖ Kapsam dışı | Kabul edilmiş ürün kararı nedeniyle uygulanmayacak |

## Özet

| Statü | Adet |
|---|---:|
| ✅ Uygulandı | 17 |
| 🟡 Kısmi | 33 |
| ⬜ Planlandı | 44 |
| ➖ Kapsam dışı | 6 |
| **Toplam** | **100** |

## Bölüm 1 — Ağ, WebSocket ve I/O Güvenliği

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 1 | Network jitter | ⬜ Planlandı | Exchange latency ölçümü ve dinamik giriş ofseti gerekli. |
| 2 | WebSocket buffer overflow | 🟡 Kısmi | Bounded channel `FullMode=Wait` backpressure ve cancellation testli; gerçek socket buffer metriği/adapter bağlantısı kaldı. [Buffer testleri](../tests/TradingBot.Application.Tests/MarketDataEventBufferTests.cs) |
| 3 | DNS resolution lag | ⬜ Planlandı | Exchange adaptörü sonrası DNS/connection lifetime politikası uygulanacak. |
| 4 | TLS handshake gecikmesi | 🟡 Kısmi | OKX REST HTTPS ve WebSocket WSS zorunlu; connection pooling/handshake metriği kaldı. [OKX stream client](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotMarketStreamClient.cs) |
| 5 | WebSocket half-open | 🟡 Kısmi | ClientWebSocket keep-alive ve uygulama ping/pong timeout'u var; reconnect supervisor metriği kaldı. [OKX stream client](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotMarketStreamClient.cs) |
| 6 | Borsa bakım modu | ⬜ Planlandı | Exchange system-status poll ve trading-ready kapısı gerekli. |
| 7 | Proxy/CDN bayat yanıt | ⬜ Planlandı | Signed timestamp/nonce ve cache-control politikası gerekli. |
| 8 | IPv4/IPv6 geçişi | ⬜ Planlandı | Deployment ortamında ölçüme dayalı address-family politikası gerekli. |
| 9 | Reconnection storm | 🟡 Kısmi | Hosted OKX supervisor bounded exponential backoff + jitter uyguluyor; retry/reconnect metriği ve uzun süreli chaos testi kaldı. [OKX worker](../src/TradingBot.Host/OkxTradingWorker.cs) |
| 10 | Paket kaybı/sequence | 🟡 Kısmi | Genel incremental session gap'te fail-closed; OKX books5 her mesajı full snapshot olarak uygular. Incremental OKX `books` ve uzun chaos testi kaldı. [Session testleri](../tests/TradingBot.Application.Tests/MarketDataStreamSessionTests.cs) |
| 11 | REST/WebSocket tutarsızlığı | 🟡 Kısmi | OKX REST snapshot `seqId` authority'si recovery portuna bağlandı; WebSocket `prevSeqId/seqId` adapter'ı kaldı. [OKX contract testleri](../tests/TradingBot.Infrastructure.Tests/OkxSpotMarketSnapshotClientTests.cs) |
| 12 | Bölgesel ağ blokajı | ⬜ Planlandı | Runbook ve onaylı failover network tasarımı gerekli. |
| 13 | Partial network writes | 🟡 Kısmi | Fragmented WebSocket text frame'leri pooled buffer ve bounded 64 KiB limit ile birleştiriliyor; sentetik fragmentation transport testi/Pipelines değerlendirmesi kaldı. [OKX stream client](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotMarketStreamClient.cs) |
| 14 | API versiyon değişimi | 🟡 Kısmi | İlk gerçek adapter OKX V5 namespace ve application portu arkasında izole; changelog/contract CI izlemesi kaldı. [OKX adapter](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotMarketSnapshotClient.cs) |
| 15 | Socket exhaustion | ⬜ Planlandı | Gerçek HTTP adaptöründe factory/uzun ömürlü handler uygulanacak. |

## Bölüm 2 — .NET Eşzamanlılık ve Bellek

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 16 | İzlenmeyen background task | 🟡 Kısmi | OKX producer task'ı session tarafından await ediliyor ve supervisor Generic Host tarafından sahipleniliyor; outbox dispatcher supervisor'ı kaldı. [Stream session](../src/TradingBot.Application/MarketData/MarketDataStreamSession.cs) |
| 17 | Thread-pool starvation | ✅ Uygulandı | Üretim kodunda `.Result`/`.Wait()` yok; async akış testlerle derleniyor. |
| 18 | LOH parçalanması | ⬜ Planlandı | Büyük WebSocket/tarihsel veri buffer'ları oluştuğunda `ArrayPool<T>` ve allocation benchmark gerekli. |
| 19 | Closure referans sızıntısı | 🟡 Kısmi | Kritik EF configuration callback'leri static; tüm hot-path closure'ları için analyzer/benchmark gerekli. |
| 20 | Async deadlock | 🟡 Kısmi | Async transaction ve I/O var; gelecekteki paylaşılan state için async-lock politikası/testi gerekli. |
| 21 | String allocation | ⬜ Planlandı | Market-data hot path oluştuğunda allocation bütçesi ve structured logging uygulanacak. |
| 22 | ConcurrentDictionary factory | 🟡 Kısmi | Instrument guard registry'sinin `GetOrAdd` factory'si static ve dış yan etkisiz; paralel yarış/stress testi kaldı. [Snapshot service](../src/TradingBot.Application/MarketSnapshotService.cs) |
| 23 | CancellationToken | ✅ Uygulandı | Application, repository ve Unit of Work async sözleşmelerinde token taşınıyor. [Unit of Work](../src/TradingBot.Infrastructure/Persistence/TradingUnitOfWork.cs) |
| 24 | Boxing/unboxing | ⬜ Planlandı | Hot-path profiling sonrası generic/value-type iyileştirmeleri yapılacak. |
| 25 | ValueTask kuralları | ✅ Uygulandı | Mevcut `ValueTask` tek await/return sözleşmesiyle kullanılıyor. [Market data portu](../src/TradingBot.Application/Abstractions/IMarketDataClient.cs) |
| 26 | Event subscription leak | ⬜ Planlandı | Stream/event abonelikleri eklendiğinde async-disposable yaşam döngüsü gerekli. |
| 27 | Pinned memory | ⬜ Planlandı | Native/pinned buffer henüz yok; eklenirse profiling ve bounded lifetime zorunlu. |
| 28 | Singleton/scoped karışımı | ✅ Uygulandı | DbContext/repository/UoW scoped; OKX hosted worker her ekonomik event için ayrı async scope açıyor ve scoped state saklamıyor. [OKX worker](../src/TradingBot.Host/OkxTradingWorker.cs) |
| 29 | Büyük dosya okuma | ✅ Uygulandı | CSV raw hash ve candle parse 64 KiB buffer ile async streaming; 25.000 candle fixture exact count/range ile testli, `ReadAllLines` yok. [CSV dataset testleri](../tests/TradingBot.Infrastructure.Tests/CsvHistoricalCandleDatasetTests.cs) |
| 30 | AsyncLocal veri kayması | ⬜ Planlandı | Correlation context eklendiğinde immutable scope ve paralellik testi gerekli. |

## Bölüm 3 — Finansal Matematik ve Veri Doğruluğu

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 31 | Price tick size | ✅ Uygulandı | OKX `tickSz` dinamik okunuyor; fiyat aşağı adım normalizasyonu ve contract/domain testleri mevcut. [Instrument catalog](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotInstrumentCatalog.cs) |
| 32 | Lot size | ✅ Uygulandı | OKX `lotSz` ve `minSz` dinamik okunuyor; miktar aşağı adım normalizasyonu ve testler mevcut. [Instrument testleri](../tests/TradingBot.Domain.Tests/InstrumentTests.cs) |
| 33 | MinNotional/order decay | 🟡 Kısmi | OKX minimum quantity (`minSz`) startup'ta doğrulanıyor ve Domain min quantity/notional reddi var. Public instrument yanıtı minimum notional sağlamadığından bu değer uydurulmuyor; gerçek account/ürün kuralı ve çok kademeli order-decay politikası kaldı. |
| 34 | Komisyon kaybı | 🟡 Kısmi | Quote-fee, PnL/persistence ve paper fill komisyonu SQL settlement'ta; backtest alış/satış maliyetleri de net return'de testli. Exchange fee-asset çeşitleri ve live parity henüz yok. [Backtest execution testleri](../tests/TradingBot.Application.Tests/BacktestExecutionSimulatorTests.cs) |
| 35 | Mum gap filling | ✅ Uygulandı | Canlı `15m/1H` candle stream timeframe başına guard ile gap'i durduruyor; observed candle dahil bounded REST aralığı atomik tamamlanmadan pipeline yeniden açılmıyor. [Candle session testleri](../tests/TradingBot.Application.Tests/ClosedCandleStreamSessionTests.cs) |
| 36 | Look-ahead bias | ✅ Uygulandı | Streaming replay yalnız bilinen trend verisini alır; execution aynı candle'da fill etmez ve next-open likiditesi için mevcut candle toplam hacmi yerine önceki kapalı candle hacmini kullanır. [Backtest execution testleri](../tests/TradingBot.Application.Tests/BacktestExecutionSimulatorTests.cs) |
| 37 | Unix epoch overflow | ⬜ Planlandı | Exchange timestamp value object ve sınır testleri gerekli. |
| 38 | Maksimum DCA adımı | ⬜ Planlandı | DCA ilk sürümde yok; eklenmeden önce maksimum kademe invariant'ı ve yeni kapsam kararı gerekir. |
| 39 | Warm-up period | ✅ Uygulandı | OKX startup kapısı aynı UTC bilgi anında sıralı `15m/200` signal ve `1H/200` trend geçmişi ister; iki seri exact, kapalı ve contiguous olmadan readiness açılmaz. [Dual warm-up testleri](../tests/TradingBot.Host.Tests/OkxInstrumentStartupGateTests.cs) |
| 40 | Sell slippage/depth | 🟡 Kısmi | Sell bid referansı, aleyhte slippage ve görünür likidite katılım sınırı var; cumulative multi-level depth henüz yok. [Paper execution](../src/TradingBot.Domain/Execution/PaperExecution.cs) |
| 41 | Spike koruması | ⬜ Planlandı | Fiyat sapma doğrulaması henüz yok; stale-data kontrolü spike kontrolü sayılmaz. |
| 42 | Leverage sync | ➖ Kapsam dışı | Kaldıraç/Futures yasak. [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) |
| 43 | Cross/isolated margin | ➖ Kapsam dışı | Margin yasak. [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) |
| 44 | Düşük likidite | ⬜ Planlandı | 24h volume, spread ve depth filtresi gerekli. |
| 45 | Gerçekçi fill süresi | 🟡 Kısmi | Paper ve backtest aynı minimum latency/slippage/participation motorunu kullanıyor; backtest next-open proxy ve pending target taşır. Order-book queue ve cancel latency henüz yok. [Backtest execution testleri](../tests/TradingBot.Application.Tests/BacktestExecutionSimulatorTests.cs) |

## Bölüm 4 — Borsa API ve Risk Yönetimi

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 46 | Rate-limit score | ⬜ Planlandı | Weight header parser ve merkezi limiter gerekli. |
| 47 | Açık emir limiti | ✅ Uygulandı | Risk profili maksimum açık emir sayısını reddediyor. [RiskEngine](../src/TradingBot.Domain/Risk/RiskEngine.cs) |
| 48 | Cancel ratio | ⬜ Planlandı | Cancel/fill metriği ve throttle gerekli. |
| 49 | Account freeze | 🟡 Kısmi | `canTrade=false` kalıcı halt üretiyor; iki temiz snapshot ve operatör kanıtı olmadan açılamıyor. Gerçek exchange account adaptörü henüz yok. [Recovery SQL testi](../tests/TradingBot.Infrastructure.Tests/SpotReconciliationIntegrationTests.cs) |
| 50 | Yetersiz bakiye | 🟡 Kısmi | Kalıcı rezervasyon ve exchange/local balance reconciliation farkta halt üretiyor; gerçek account adaptörü ve kontrollü state correction henüz yok. [Reconciliation motoru](../src/TradingBot.Domain/Reconciliation/SpotReconciliation.cs) |
| 51 | Kaldıraç kısıtlaması | ➖ Kapsam dışı | Kaldıraç yok. [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) |
| 52 | Asset exposure | 🟡 Kısmi | Sembol ve gross exposure var; sektör/korelasyon limiti henüz yok. [RiskProfile](../src/TradingBot.Domain/Risk/RiskProfile.cs) |
| 53 | Max position notional | ✅ Uygulandı | Sembol/gross notional capacity position sizing'e uygulanıyor. [Risk testleri](../tests/TradingBot.Domain.Tests/RiskEngineTests.cs) |
| 54 | Hedge/one-way mode | ➖ Kapsam dışı | Spot-only ve negatif pozisyon yasak. |
| 55 | Server-side stop | ⬜ Planlandı | Seçilecek Spot borsanın server-side stop adapter'i gerekli. |
| 56 | Self-trade prevention | ⬜ Planlandı | Açık emir çapraz kontrolü ve borsa STP özelliği gerekli. |
| 57 | Botlar arası etkileşim | ✅ Uygulandı | Benzersiz `ClientOrderId` yanında `(Exchange, ExchangeExecutionId)` anahtarı da duplicate ekonomik fill'i engelliyor. [Portfolio entegrasyon testi](../tests/TradingBot.Infrastructure.Tests/PortfolioPersistenceIntegrationTests.cs) |
| 58 | Liquidation yakınlığı | ➖ Kapsam dışı | Kaldıraç ve liquidation yok. |
| 59 | Funding spread | ➖ Kapsam dışı | Spot-only; funding yok. |
| 60 | Amend/cancel-replace yarışı | 🟡 Kısmi | Order ve kalıcı reservation aynı transaction'da fill-first/cancel-first yarışını koruyor; exchange amend adapter'i henüz yok. [Reservation use case testleri](../tests/TradingBot.Application.Tests/SpotOrderReservationUseCaseTests.cs) |

## Bölüm 5 — Güvenlik, Kimlik Doğrulama ve Loglama

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 61 | Secret log sızıntısı | 🟡 Kısmi | Secret kaynak koda yazılmıyor; merkezi log redaction/masking henüz yok. |
| 62 | HMAC timestamp/recvWindow | ⬜ Planlandı | Exchange clock offset ve signed-request policy gerekli. |
| 63 | API key bellek riski | ⬜ Planlandı | Vault provider, dar secret lifetime ve rotation gerekli. |
| 64 | Yetkisiz Telegram komutu | ⬜ Planlandı | Komut kanalı seçilmedi; eklenirse allowlist/auth zorunlu. |
| 65 | Veritabanı şifreleme | ⬜ Planlandı | Production TDE/volume encryption ve backup encryption kararı gerekli. |
| 66 | Dangling API key | 🟡 Kısmi | `.env`/production config ignore ve CI tracked-file secret assignment/forbidden-file kapısı var; GitHub native secret scanning ve geçmiş taraması kaldı. [Repository policy](../scripts/Test-RepositoryPolicy.ps1) |
| 67 | Senkron/aşırı log I/O | ⬜ Planlandı | Async structured rolling sink gerekli. |
| 68 | MITM | 🟡 Kısmi | OKX adapter HTTPS dışı base address'i fail-fast reddediyor ve TLS doğrulaması kapatılmıyor; certificate policy/pinning kararı kaldı. [OKX adapter](../src/TradingBot.Infrastructure/Integrations/Okx/OkxSpotMarketSnapshotClient.cs) |
| 69 | SSH güvenliği | ⬜ Planlandı | Deployment hardening runbook'u gerekli. |
| 70 | Log rotasyonu | ⬜ Planlandı | Retention/rotation ve disk alarmı gerekli. |
| 71 | Alert fatigue | ⬜ Planlandı | Dedup/throttle/batch notification pipeline gerekli. |
| 72 | NuGet vulnerability | ✅ Uygulandı | CI transitif NuGet vulnerability raporunu JSON üretip bulgu varsa job'ı durduruyor; yerel tarama da kalite kapısıdır. [CI workflow](../.github/workflows/ci.yml) |
| 73 | Runtime watchdog | 🟡 Kısmi | `/health/ready`, Spot metadata kapısı ve ilk geçerli market event tamamlanana kadar veya stream kopunca 503 döndürüyor. SQL/reconciliation dependency, ayrı startup probe ve bağımsız watchdog kaldı. [Program](../src/TradingBot.Host/Program.cs) |
| 74 | Yedek bildirim kanalı | ⬜ Planlandı | Birincil/yedek kanal seçimi ve failover testi gerekli. |
| 75 | Güvenlik güncellemeleri | ⬜ Planlandı | OS/runtime patch runbook ve image scanning gerekli. |

## Bölüm 6 — Algoritma ve Mantıksal Hatalar

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 76 | Closed-candle sinyali | 🟡 Kısmi | EMA20/EMA200 v1 decision engine ve streaming replay yalnız kapanmış candle kullanıyor; canlı series-store tetiklemesi ve execution bağlantısı kaldı. [Strategy evaluator testleri](../tests/TradingBot.Domain.Tests/LongFlatStrategyEvaluatorTests.cs) |
| 77 | FOMO koruması | 🟡 Kısmi | Pozitif `15m` candle gövdesi `%2`yi aşınca cross-up girişi bloklanıyor; eşik için out-of-sample/volatilite kanıtı kaldı. [Strategy evaluator testleri](../tests/TradingBot.Domain.Tests/LongFlatStrategyEvaluatorTests.cs) |
| 78 | Trailing-stop flaw | ⬜ Planlandı | Volatilite tabanlı mesafe ve monotonic stop testleri gerekli. |
| 79 | İndikatör çelişkisi | ⬜ Planlandı | Ağırlıklı scoring sözleşmesi gerekli. |
| 80 | Regime switching | 🟡 Kısmi | Deterministik `1H close > EMA200` makro trend filtresi mevcut; range/ADX benzeri rejim ayrımı ve out-of-sample kanıtı kaldı. [EMA trend testleri](../tests/TradingBot.Domain.Tests/EmaTrendFilterTests.cs) |
| 81 | News/event blackout | ⬜ Planlandı | Güvenilir ekonomik takvim adapter'i ve safe mode gerekli. |
| 82 | Risk/reward dengesizliği | ⬜ Planlandı | Minimum reward/risk ve exit-policy testleri gerekli. |
| 83 | Grid trap | ⬜ Planlandı | Grid ilk sürümde yok; eklenirse sermaye tüketimi ve maksimum kademe koruması zorunlu. |
| 84 | Timeframe senkronu | ✅ Uygulandı | `15m/1H` exact-multiple strategy invariant'ı, UTC boundary, future-data reddi ve canlı channel-to-timeframe allowlist'i testli; iki stream bağımsız guard ve ortak reconnect anchor bilgisi taşır. [OKX candle parser testleri](../tests/TradingBot.Infrastructure.Tests/OkxCandleMessageParserTests.cs) |
| 85 | Black-swan stop | ⬜ Planlandı | Spot server-side hard stop ve emergency policy gerekli. |
| 86 | Çift borsa arbitrajı | ⬜ Planlandı | İlk kapsam tek Spot borsası; gelecekte eklenirse iki bacaklı execution/saga koruması gerekli. |
| 87 | NaN/Infinity | ✅ Uygulandı | EMA yalnız finite `decimal` OHLC girdisiyle checked arithmetic kullanır; yetersiz/gap'li seri ve decimal overflow karar üretmeden reddedilir. [EMA uygulaması](../src/TradingBot.Domain/Strategies/ExponentialMovingAverage.cs) |
| 88 | Korelasyon körlüğü | ⬜ Planlandı | Portfolio correlation/sector exposure modeli gerekli. |
| 89 | Overfitting | 🟡 Kısmi | Chronological/OOS kilidi, rolling/expanding schedule, schedule/run/report hash, çoklu OOS agregasyonu ve normalize SQL persistence testli; tarihsel orchestration ile gerçek çoklu-rejim OOS kanıtı kaldı. [Walk-forward rapor testleri](../tests/TradingBot.Application.Tests/WalkForwardReportTests.cs) |
| 90 | Order state machine | ✅ Uygulandı | Geçişler, terminal state, partial fill ve cancel/fill yarışı order+reservation+portfolio SQL transaction'ında testli. [SQL entegrasyon testi](../tests/TradingBot.Infrastructure.Tests/SpotOrderReservationIntegrationTests.cs) |

## Bölüm 7 — DevOps, Deployment ve Süreç

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 91 | Açık pozisyonda deploy | ⬜ Planlandı | Deployment precondition ve zero-position guard gerekli. |
| 92 | Kontrolsüz OS restart | ⬜ Planlandı | Maintenance window ve service auto-recovery runbook'u gerekli. |
| 93 | Altyapı failover | ⬜ Planlandı | Single-active ownership/reconciliation çözülmeden failover açılmayacak. |
| 94 | DB/tick I/O şişmesi | 🟡 Kısmi | 100 ms stream bütünüyle integrity'den geçerken SQL execution yapılandırılmış aralıkta örnekleniyor; market-data persistence batch/retention kaldı. [OKX worker](../src/TradingBot.Host/OkxTradingWorker.cs) |
| 95 | Telemetry | ⬜ Planlandı | OpenTelemetry/Prometheus/Grafana kararı ve dashboard gerekli. |
| 96 | Graceful shutdown | 🟡 Kısmi | Generic Host cancellation market client, cycle, repository ve polling delay'e taşınıyor; açık emir iptal politikası/checkpoint henüz yok. [TradingWorker](../src/TradingBot.Host/TradingWorker.cs) |
| 97 | Clock drift | ⬜ Planlandı | OS NTP/chrony kontrolü ve exchange offset metriği gerekli. |
| 98 | Environment mix-up | 🟡 Kısmi | Trading yalnız Paper; OKX public endpoint/instrument startup'ta fail-fast ve CI secretsiz ağsız job olarak ayrık. Testnet/live credential ve deployment pipeline'ları henüz yok. [CI workflow](../.github/workflows/ci.yml) |
| 99 | Global kill switch | 🟡 Kısmi | RiskEngine kill-switch ve kalıcı reconciliation halt yeni exposure'ı reddediyor; recovery kanıtlı ve audit'li. Global flatten mekanizması yok. [Recovery use case](../src/TradingBot.Application/Reconciliation/RecoverTradingSafety.cs) |
| 100 | Manuel müdahale | 🟡 Kısmi | Harici fark halt ediliyor; iki temiz snapshot+operatör onaylı recovery ve eski risk kararı reddi var. User stream ve kontrollü state correction henüz yok. [Recovery SQL testi](../tests/TradingBot.Infrastructure.Tests/SpotReconciliationIntegrationTests.cs) |

## Güncelleme politikası

- Her feature PR, etkilediği satırların statüsünü ve kanıtını günceller.
- `✅ Uygulandı` statüsü otomatik test veya operasyonel doğrulama olmadan verilemez.
- `➖ Kapsam dışı` statüsü ADR bağlantısı gerektirir.
- Kısmi kontrol production-ready kabul edilmez.
- Matris satır numaraları ve statü özeti CI repository-policy script'iyle doğrulanır.
- Yeni bir anayasa maddesi eklenirse numara, kabul ölçütü ve roadmap işi aynı değişiklikte eklenir.
