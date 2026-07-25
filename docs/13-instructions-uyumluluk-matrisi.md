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
| ✅ Uygulandı | 10 |
| 🟡 Kısmi | 21 |
| ⬜ Planlandı | 63 |
| ➖ Kapsam dışı | 6 |
| **Toplam** | **100** |

## Bölüm 1 — Ağ, WebSocket ve I/O Güvenliği

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 1 | Network jitter | ⬜ Planlandı | Exchange latency ölçümü ve dinamik giriş ofseti gerekli. |
| 2 | WebSocket buffer overflow | ⬜ Planlandı | Bounded channel, socket buffer ölçümü ve backpressure testi gerekli. |
| 3 | DNS resolution lag | ⬜ Planlandı | Exchange adaptörü sonrası DNS/connection lifetime politikası uygulanacak. |
| 4 | TLS handshake gecikmesi | ⬜ Planlandı | `IHttpClientFactory`, HTTP/2/keep-alive ve connection pooling gerekli. |
| 5 | WebSocket half-open | ⬜ Planlandı | Heartbeat, TCP keep-alive ve stale-stream watchdog gerekli. |
| 6 | Borsa bakım modu | ⬜ Planlandı | Exchange system-status poll ve trading-ready kapısı gerekli. |
| 7 | Proxy/CDN bayat yanıt | ⬜ Planlandı | Signed timestamp/nonce ve cache-control politikası gerekli. |
| 8 | IPv4/IPv6 geçişi | ⬜ Planlandı | Deployment ortamında ölçüme dayalı address-family politikası gerekli. |
| 9 | Reconnection storm | ⬜ Planlandı | Exponential backoff + full jitter uygulanacak. |
| 10 | Paket kaybı/sequence | ⬜ Planlandı | WebSocket sequence doğrulaması ve gap recovery gerekli. |
| 11 | REST/WebSocket tutarsızlığı | ⬜ Planlandı | Event-time/sequence authority ve reconciliation gerekli. |
| 12 | Bölgesel ağ blokajı | ⬜ Planlandı | Runbook ve onaylı failover network tasarımı gerekli. |
| 13 | Partial network writes | ⬜ Planlandı | Stream framing ve parçalı payload testleri gerekli. |
| 14 | API versiyon değişimi | 🟡 Kısmi | Ports & Adapters kararı var; gerçek versioned exchange adapter henüz yok. [Mimari](02-mimari.md) |
| 15 | Socket exhaustion | ⬜ Planlandı | Gerçek HTTP adaptöründe factory/uzun ömürlü handler uygulanacak. |

## Bölüm 2 — .NET Eşzamanlılık ve Bellek

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 16 | İzlenmeyen background task | 🟡 Kısmi | Worker host tarafından sahipleniliyor; ilerideki tüm dispatcher/stream task'ları için supervisor gerekli. [TradingWorker](../src/TradingBot.Host/TradingWorker.cs) |
| 17 | Thread-pool starvation | ✅ Uygulandı | Üretim kodunda `.Result`/`.Wait()` yok; async akış testlerle derleniyor. |
| 18 | LOH parçalanması | ⬜ Planlandı | Büyük WebSocket/tarihsel veri buffer'ları oluştuğunda `ArrayPool<T>` ve allocation benchmark gerekli. |
| 19 | Closure referans sızıntısı | 🟡 Kısmi | Kritik EF configuration callback'leri static; tüm hot-path closure'ları için analyzer/benchmark gerekli. |
| 20 | Async deadlock | 🟡 Kısmi | Async transaction ve I/O var; gelecekteki paylaşılan state için async-lock politikası/testi gerekli. |
| 21 | String allocation | ⬜ Planlandı | Market-data hot path oluştuğunda allocation bütçesi ve structured logging uygulanacak. |
| 22 | ConcurrentDictionary factory | ⬜ Planlandı | Cache/registry eklendiğinde side-effect-free factory testi gerekli. |
| 23 | CancellationToken | ✅ Uygulandı | Application, repository ve Unit of Work async sözleşmelerinde token taşınıyor. [Unit of Work](../src/TradingBot.Infrastructure/Persistence/TradingUnitOfWork.cs) |
| 24 | Boxing/unboxing | ⬜ Planlandı | Hot-path profiling sonrası generic/value-type iyileştirmeleri yapılacak. |
| 25 | ValueTask kuralları | ✅ Uygulandı | Mevcut `ValueTask` tek await/return sözleşmesiyle kullanılıyor. [Market data portu](../src/TradingBot.Application/Abstractions/IMarketDataClient.cs) |
| 26 | Event subscription leak | ⬜ Planlandı | Stream/event abonelikleri eklendiğinde async-disposable yaşam döngüsü gerekli. |
| 27 | Pinned memory | ⬜ Planlandı | Native/pinned buffer henüz yok; eklenirse profiling ve bounded lifetime zorunlu. |
| 28 | Singleton/scoped karışımı | ✅ Uygulandı | DbContext/repository/UoW scoped, stateless ID generator singleton kayıtlı. [DI](../src/TradingBot.Infrastructure/DependencyInjection.cs) |
| 29 | Büyük dosya okuma | ⬜ Planlandı | Backtest reader streaming olacak; büyük CSV fixture testi gerekli. |
| 30 | AsyncLocal veri kayması | ⬜ Planlandı | Correlation context eklendiğinde immutable scope ve paralellik testi gerekli. |

## Bölüm 3 — Finansal Matematik ve Veri Doğruluğu

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 31 | Price tick size | ✅ Uygulandı | Fiyat aşağı adım normalizasyonu ve test mevcut. [Instrument](../src/TradingBot.Domain/Instruments/Instrument.cs) |
| 32 | Lot size | ✅ Uygulandı | Miktar aşağı adım normalizasyonu ve test mevcut. [Instrument testleri](../tests/TradingBot.Domain.Tests/InstrumentTests.cs) |
| 33 | MinNotional/order decay | 🟡 Kısmi | Min quantity/notional reddi var; çok kademeli order-decay politikası henüz yok. |
| 34 | Komisyon kaybı | 🟡 Kısmi | Quote-fee, PnL/persistence ve paper fill komisyonu var; exchange fee-asset çeşitleri ve live parity henüz yok. [Paper execution testleri](../tests/TradingBot.Domain.Tests/PaperExecutionEngineTests.cs) |
| 35 | Mum gap filling | ⬜ Planlandı | REST snapshot + WebSocket sequence recovery gerekli. |
| 36 | Look-ahead bias | ⬜ Planlandı | Backtest engine ve future-data guard testleri gerekli. |
| 37 | Unix epoch overflow | ⬜ Planlandı | Exchange timestamp value object ve sınır testleri gerekli. |
| 38 | Maksimum DCA adımı | ⬜ Planlandı | DCA ilk sürümde yok; eklenmeden önce maksimum kademe invariant'ı ve yeni kapsam kararı gerekir. |
| 39 | Warm-up period | ⬜ Planlandı | Strateji indikatör lookback doğrulaması gerekli. |
| 40 | Sell slippage/depth | 🟡 Kısmi | Sell bid referansı, aleyhte slippage ve görünür likidite katılım sınırı var; cumulative multi-level depth henüz yok. [Paper execution](../src/TradingBot.Domain/Execution/PaperExecution.cs) |
| 41 | Spike koruması | ⬜ Planlandı | Fiyat sapma doğrulaması henüz yok; stale-data kontrolü spike kontrolü sayılmaz. |
| 42 | Leverage sync | ➖ Kapsam dışı | Kaldıraç/Futures yasak. [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) |
| 43 | Cross/isolated margin | ➖ Kapsam dışı | Margin yasak. [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) |
| 44 | Düşük likidite | ⬜ Planlandı | 24h volume, spread ve depth filtresi gerekli. |
| 45 | Gerçekçi fill süresi | 🟡 Kısmi | Minimum latency, limit koşulu ve likidite kaynaklı waiting/partial fill var; queue position ve cancel latency henüz yok. [Paper execution testleri](../tests/TradingBot.Domain.Tests/PaperExecutionEngineTests.cs) |

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
| 66 | Dangling API key | 🟡 Kısmi | `.env` ve production config ignore; secret scanner/CI henüz yok. [.gitignore](../.gitignore) |
| 67 | Senkron/aşırı log I/O | ⬜ Planlandı | Async structured rolling sink gerekli. |
| 68 | MITM | 🟡 Kısmi | TLS doğrulamasını kapatan üretim kodu yok; production certificate policy/pinning kararı gerekli. |
| 69 | SSH güvenliği | ⬜ Planlandı | Deployment hardening runbook'u gerekli. |
| 70 | Log rotasyonu | ⬜ Planlandı | Retention/rotation ve disk alarmı gerekli. |
| 71 | Alert fatigue | ⬜ Planlandı | Dedup/throttle/batch notification pipeline gerekli. |
| 72 | NuGet vulnerability | 🟡 Kısmi | Manuel transitif tarama temiz; CI zorunlu kapısı henüz yok. [Test stratejisi](07-test-stratejisi.md) |
| 73 | Runtime watchdog | 🟡 Kısmi | Basit `/health` var; bağımsız watchdog ve liveness/readiness/startup ayrımı yok. [Program](../src/TradingBot.Host/Program.cs) |
| 74 | Yedek bildirim kanalı | ⬜ Planlandı | Birincil/yedek kanal seçimi ve failover testi gerekli. |
| 75 | Güvenlik güncellemeleri | ⬜ Planlandı | OS/runtime patch runbook ve image scanning gerekli. |

## Bölüm 6 — Algoritma ve Mantıksal Hatalar

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 76 | Closed-candle sinyali | ⬜ Planlandı | Strateji engine henüz yok; closed-candle invariant/test gerekli. |
| 77 | FOMO koruması | ⬜ Planlandı | Strateji momentum/spike giriş filtresi gerekli. |
| 78 | Trailing-stop flaw | ⬜ Planlandı | Volatilite tabanlı mesafe ve monotonic stop testleri gerekli. |
| 79 | İndikatör çelişkisi | ⬜ Planlandı | Ağırlıklı scoring sözleşmesi gerekli. |
| 80 | Regime switching | ⬜ Planlandı | Trend/range filtresi ve out-of-sample testi gerekli. |
| 81 | News/event blackout | ⬜ Planlandı | Güvenilir ekonomik takvim adapter'i ve safe mode gerekli. |
| 82 | Risk/reward dengesizliği | ⬜ Planlandı | Minimum reward/risk ve exit-policy testleri gerekli. |
| 83 | Grid trap | ⬜ Planlandı | Grid ilk sürümde yok; eklenirse sermaye tüketimi ve maksimum kademe koruması zorunlu. |
| 84 | Timeframe senkronu | ⬜ Planlandı | Exchange UTC candle boundary value object/test gerekli. |
| 85 | Black-swan stop | ⬜ Planlandı | Spot server-side hard stop ve emergency policy gerekli. |
| 86 | Çift borsa arbitrajı | ⬜ Planlandı | İlk kapsam tek Spot borsası; gelecekte eklenirse iki bacaklı execution/saga koruması gerekli. |
| 87 | NaN/Infinity | ⬜ Planlandı | İndikatör katmanı geldiğinde finite-output guard testleri gerekli. |
| 88 | Korelasyon körlüğü | ⬜ Planlandı | Portfolio correlation/sector exposure modeli gerekli. |
| 89 | Overfitting | ⬜ Planlandı | Walk-forward, out-of-sample ve data/version kayıtları gerekli. |
| 90 | Order state machine | ✅ Uygulandı | Geçişler, terminal state, partial fill ve cancel/fill yarışı order+reservation+portfolio SQL transaction'ında testli. [SQL entegrasyon testi](../tests/TradingBot.Infrastructure.Tests/SpotOrderReservationIntegrationTests.cs) |

## Bölüm 7 — DevOps, Deployment ve Süreç

| No | Kural | Statü | Kanıt veya kalan iş |
|---:|---|---|---|
| 91 | Açık pozisyonda deploy | ⬜ Planlandı | Deployment precondition ve zero-position guard gerekli. |
| 92 | Kontrolsüz OS restart | ⬜ Planlandı | Maintenance window ve service auto-recovery runbook'u gerekli. |
| 93 | Altyapı failover | ⬜ Planlandı | Single-active ownership/reconciliation çözülmeden failover açılmayacak. |
| 94 | DB/tick I/O şişmesi | ⬜ Planlandı | Batch insert, partition/retention ve disk metriği gerekli. |
| 95 | Telemetry | ⬜ Planlandı | OpenTelemetry/Prometheus/Grafana kararı ve dashboard gerekli. |
| 96 | Graceful shutdown | 🟡 Kısmi | Generic Host cancellation var; açık emir politikası/checkpoint henüz yok. |
| 97 | Clock drift | ⬜ Planlandı | OS NTP/chrony kontrolü ve exchange offset metriği gerekli. |
| 98 | Environment mix-up | 🟡 Kısmi | Paper varsayılan ve config fail-fast var; ayrık CI credential/pipeline henüz yok. [TradingOptions](../src/TradingBot.Host/TradingOptions.cs) |
| 99 | Global kill switch | 🟡 Kısmi | RiskEngine kill-switch ve kalıcı reconciliation halt yeni exposure'ı reddediyor; recovery kanıtlı ve audit'li. Global flatten mekanizması yok. [Recovery use case](../src/TradingBot.Application/Reconciliation/RecoverTradingSafety.cs) |
| 100 | Manuel müdahale | 🟡 Kısmi | Harici fark halt ediliyor; iki temiz snapshot+operatör onaylı recovery ve eski risk kararı reddi var. User stream ve kontrollü state correction henüz yok. [Recovery SQL testi](../tests/TradingBot.Infrastructure.Tests/SpotReconciliationIntegrationTests.cs) |

## Güncelleme politikası

- Her feature PR, etkilediği satırların statüsünü ve kanıtını günceller.
- `✅ Uygulandı` statüsü otomatik test veya operasyonel doğrulama olmadan verilemez.
- `➖ Kapsam dışı` statüsü ADR bağlantısı gerektirir.
- Kısmi kontrol production-ready kabul edilmez.
- Matris özeti satır statülerinden CI ile doğrulanacak otomasyon kurulana kadar manuel olarak iki kez kontrol edilir.
- Yeni bir anayasa maddesi eklenirse numara, kabul ölçütü ve roadmap işi aynı değişiklikte eklenir.
