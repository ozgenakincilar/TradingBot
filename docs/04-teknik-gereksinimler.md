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
- Market event buffer bounded `Channel<T>` ve `FullMode=Wait` kullanır; write/read operasyonları cancellation token taşır.
- Replay hizalaması iki aşamalıdır: önce snapshot ve tüm buffered seri doğrulanır, yalnız tamamen contiguous sonuç downstream'e topluca açılır.
- Replay ortasında gap, conflicting sequence veya timestamp regression görülürse kısmi event listesi yayınlanmaz ve yeni recovery gerekir.

## 4. Ağ ve HTTP

- `IHttpClientFactory` veya uzun ömürlü handler bağlantı havuzu kullanılır.
- Timeout her çağrı türünde açıkça belirlenir.
- Retry sadece idempotent ve transient işlemlerde uygulanır.
- Exponential backoff + full jitter zorunludur.
- Rate-limit weight ve response header’ları izlenir.
- WebSocket heartbeat, stale stream, sequence gap ve reconnect yönetilir.
- Her instrument bağımsız sequence cursor'u taşır; ilk yayın öncesi REST recovery snapshot ile hizalanır.
- Gap veya çelişkili sequence algılandığında son güvenilir cursor korunur ve yeni market event trading hattına yayınlanmaz.
- Recovery snapshot son kabul edilen sequence, event time veya receive time değerini geriye saramaz.
- Freshness yalnız integrity state ready ise ve son receive time yapılandırılmış maksimum yaşı aşmıyorsa doğrudur.
- `IMarketDataClient`, normal top-of-book event'i ile authoritative recovery snapshot çağrısını ayrı port metotları olarak sunar; her ikisi sequence, event time ve receive time taşır.
- İlk gerçek REST recovery adapter'ı OKX TR V5 `GET /api/v5/market/books?sz=1` yanıtındaki `seqId`, `ts`, best bid/ask ve miktarları normalize eder.
- OKX Spot instrument catalog `GET /api/v5/public/instruments?instType=SPOT&instId={BASE-QUOTE}` çağrısından `instType`, `instId`, `baseCcy`, `quoteCcy`, `tickSz`, `lotSz`, `minSz` ve `state` alanlarını normalize eder.
- Host, OKX worker başlamadan önce instrument metadata'sını fail-fast doğrular. Instrument `SPOT/live` değilse veya tick/lot/minimum quantity pozitif değilse process trading-ready olmadan durur.
- OKX `minSz` minimum base-asset miktarıdır; minimum notional değildir. Ayrı bir borsa/account kuralı veya açık risk politikası olmadan minimum notional uydurulmaz.
- OKX adapter'ı yalnız HTTPS base address ve `OKX/BASE-QUOTE` instrument kabul eder; API serbest metin hata mesajını exception/log sınırına taşımaz.
- OKX order-book continuity için `seqId/prevSeqId` kullanılır; deprecated checksum doğrulama kaynağı değildir.
- OKX public market stream `wss://` üzerinden `books5` kanalına subscribe olur; fragmented text frame'leri 64 KiB bounded mesaj sınırı ve pooled receive buffer ile birleştirir.
- Stream 20 saniye sessizlikte `ping` gönderir; takip eden heartbeat penceresinde `pong` veya veri gelmezse bağlantıyı hatalı kabul eder.
- OKX `seqId` değerlerinin ardışık sayı olması beklenmez. Incremental `books` kullanılırsa continuity `prevSeqId == son seqId` ile doğrulanır; mevcut `books5` kanalında her mesaj bağımsız tam snapshot'tır ve delta zinciri gibi yorumlanmaz.
- Hosted OKX supervisor stream producer'ını bounded buffer üzerinden başlatır; REST snapshot cross-source freshness kontrolü, ilk `books5` mesajı sequence anchor'ı olur. Session producer task'ı cancellation ve exception dahil her terminal durumda await edilir.
- Stream kopması exponential backoff ve jitter ile yeniden bağlanır; her reconnect yeni REST snapshot/replay session'ı açar.
- Tüm WebSocket event'leri integrity guard'dan geçer, ancak SQL paper execution sorgusu yapılandırılmış polling aralığında örneklenir.
- `/health/ready` yalnız instrument başlangıç kapısı geçtikten ve ilk doğrulanmış market event alındıktan sonra başarılı olur; stream kopmasında yeniden başarısız duruma döner.
- `MarketSnapshotService`, duplicate/out-of-order event'i aşağı akışa vermez; gap/conflict/timestamp regression durumunda recovery snapshot ister ve recovery reddedilirse fail-closed davranır.
- TLS sertifika doğrulaması kapatılamaz.
- DNS/connection lifetime ölçülerek yapılandırılır; sabit IP’ye kör pinleme yapılmaz.

### Candle bütünlüğü

- Candle zaman aralıkları UTC ve Unix epoch tabanlı sabit `Timeframe` sınırlarına hizalanır; sunucu local saati sınır hesabında kullanılmaz.
- Yalnız kapanış zamanı gelmiş immutable OHLCV candle nesnesi oluşturulabilir; open candle strategy/backtest sözleşmesine giremez.
- Candle serisi ilk contiguous recovery uygulanmadan ready değildir. Duplicate eski candle'ı ilerletmez; gap veya aynı open time'daki çelişkili içerik seriyi fail-closed yapar.
- Gap recovery, beklenen ilk candle ile gözlenen kapalı candle arasını `IClosedCandleHistoryClient` üzerinden `[fromInclusive, toExclusive)` aralığında ister.
- Recovery çağrısı maksimum candle sayısıyla bounded'dır. Eksik, fazla, sırasız, farklı instrument/timeframe veya henüz açık candle içeren yanıtın hiçbir bölümü yayınlanmaz.
- OKX V5 `GET /api/v5/market/history-candles` adaptörü tek sayfada en fazla 300 kayıt ister; ters kronolojik cevabı sıralar ve yalnız `confirm=1` satırlarını kapalı candle olarak kabul eder.
- OKX timeframe mapping'i explicit allowlist'tir: `1s`, `1m`, `3m`, `5m`, `15m`, `30m`, `1H`, `2H`, `4H`, `6Hutc`, `12Hutc`, `1Dutc`. Calendar-month ve belirsiz UTC+8 bar'ları bu sabit-duration sözleşmesine alınmaz.
- Warm-up use case'i `knownAt` değerini UTC timeframe sınırına aşağı yuvarlar; açık mevcut candle'ı dışarıda bırakıp `[boundary - N * timeframe, boundary)` aralığını ister.
- Warm-up sayısı bounded policy'yi aşamaz. Eksik, kaymış, gap içeren veya yanlış instrument/timeframe serisi readiness üretmez; istemciden gelen liste immutable kopyaya alınır.
- OKX host başlangıcında ilk strateji sürümü için signal timeframe tam `15m`, trend timeframe tam `1H`; iki warm-up sayısı da 200-300 aralığında doğrulanır.
- Instrument kapısından sonra aynı UTC `knownAt` ile önce `15m` signal, sonra `1H` trend warm-up çalışır. İki seri de exact/contiguous 200 kapalı candle sağlamadan candle-history readiness açılmaz; herhangi bir hata host başlangıcını fail-closed durdurur.
- Readiness snapshot signal ve trend serilerinin durum, timeframe ve doğrulanmış candle sayılarını ayrı taşır.
- OKX exchange-aggregated candle akışı order-book public socket'inden ayrıdır ve yalnız `wss://.../ws/v5/business` endpoint'ine bağlanır. İlk strateji sürümü yalnız `candle15m` ve `candle1H` kanallarına subscribe olur.
- Candle parser `confirm=0` açık güncellemeyi yayınlamaz; yalnız `confirm=1` satırını UTC boundary/OHLCV invariant'larıyla `Candle`a dönüştürür. Bilinmeyen kanal, instrument, timeframe veya serbest upstream hata detayı fail-closed/sanitize edilir.
- Canlı stream REST anchor kurulurken 64 kapasiteli bounded channel'da bekletilir. Her timeframe bağımsız `ClosedCandleSequenceGuard` taşır; duplicate/out-of-order kesilir, gap en fazla 300 candle ile REST'ten atomik tamamlanır.
- Candle worker bağlantı kurulana veya reconnect anchor'ı tamamlanana kadar signal/trend readiness'i kapatır; heartbeat, stream sonu veya integrity hatasında 1-16 saniye exponential backoff ve jitter ile yeniden bağlanır.
- Warm-up ve canlı güncellemeler timeframe başına 300 kapasiteli singleton `ClosedCandleSeriesStore` içinde birleşir. Store seed edilmeden canlı candle kabul etmez; immutable snapshot verir, duplicate/eski candle'ı ilerletmez; disconnect, gap veya çelişkide kanıtı koruyup seriyi not-ready yapar.
- Her reconnect sonrasında iki timeframe de aynı UTC `knownAt` üzerinden tam warm-up ile yeniden seed edilir; stream buffer'ında bekleyen duplicate veya yeni candle'lar daha sonra deterministik olarak işlenir.

### Strateji sözleşmesi

- İlk tanım `btc-usdt-long-flat-baseline/v1`, `OKX:BTC-USDT`, `15m` sinyal ve `1H` trend timeframe'lerini taşır.
- Trend timeframe, signal timeframe'den büyük ve onun tam katı olmak zorundadır.
- `EMA(200)` için hem signal hem trend serisinde minimum warm-up 200 kapalı candle'dan kısa olamaz.
- EMA hesabı `decimal` kullanır, son tam 200 candle penceresinin ilk kapanışından seed edilir ve checked arithmetic taşmasında karar üretmeden hata verir.
- Long trend izni yalnız son kapalı `1H` candle kapanışı EMA(200)'ün kesin olarak üzerindeyken verilir; bu çıktı strategy engine/backtest kabulünden önce execution'a bağlanmaz.
- v1 signal EMA periyodu 20, maksimum pozitif signal candle gövdesi `%2`dir. Flat girişte EMA20 yukarı kesişimi; long çıkışta EMA20 aşağı kesişimi veya trend filtresi kaybı kullanılır.
- Historical replay input'u belleğe topluca yüklemez; iki `IAsyncEnumerable<Candle>` akışını bounded 200-candle pencerelerde işler. Gap, sırasız veri ve identity mismatch fail-closed hatadır.
- Decision replay sonucu doğrudan execution değildir. Ayrı backtest simulator kararı aynı candle'da değil bir sonraki `15m` open proxy'sinde `minimumLatency` sonrası değerlendirir.
- Sentetik top-of-book, next-open midpoint ± half-spread olarak kurulur; `PaperExecutionEngine` yönsel slippage, iki taraflı quote fee ve maximum liquidity participation uygular.
- Open fill'de mevcut candle'ın henüz bilinmeyen toplam hacmi kullanılmaz. Yalnız karar anında kapanmış önceki candle base volume değeri likidite proxy'sidir.
- Backtest allocation kullanılabilir cash ile sınırlıdır; leverage, borçlanma, short ve negatif Spot quantity oluşturamaz. Veri sonunda açık pozisyon zorla kapatılmaz.
- Next-open OHLCV proxy intrabar order book/queue position sağlamaz; rapor production kârlılık kanıtı veya aylık hedef garantisi değildir.

### Historical dataset ve run kimliği

- Canonical CSV header `open_time_utc,open,high,low,close,base_volume`; UTF-8, UTC round-trip timestamp ve invariant decimal zorunludur. Quoted/ekstra kolon veya serbest locale formatı kabul edilmez.
- CSV raw byte SHA-256 hesabı ve candle parse aynı salt-okunur file handle üzerinde iki streaming geçişle yapılır; 64 KiB file buffer kullanılır ve tüm dosya RAM'e alınmaz.
- Reader tek kullanımlıdır. Yalnız EOF'ye ulaşan contiguous dataset `CompletedSummary` üretir; erken consumer stop, cancellation veya parse hatası manifest kanıtı oluşturamaz.
- Manifest data/config/manifest SHA-256, dataset count/range, strategy version, execution varsayımları, split, purpose, partition listesi ve random seed taşır.
- Train/validation/OOS split'leri UTC `[start,end)` aralıklarıdır. Parameter selection OOS candle yield edemez; final evaluation yalnız OOS partition'ıyla çalışır.
- Split sınırları `15m/1H` boundary'lerine hizalı ve her iki dataset tarafından tamamen kapsanmış olmak zorundadır.
- Walk-forward schedule sabit train/validation/OOS süreleriyle rolling veya expanding üretilir. İlerleme OOS süresidir; böylece OOS pencereleri çakışmaz veya boşluk bırakmaz.
- Süreler iki timeframe'in tam katı, dataset başlangıç/bitişi iki timeframe'e hizalı olmalıdır. Tam pencere üretilememesi ve 10.000 pencere sınırının aşılması fail-closed hatadır.
- Walk-forward schedule hash zaman politikasını, run hash sıralı final-OOS manifestlerini, report hash ise tüm pencere execution metriklerini kapsar.
- Birleşik rapor yalnız schedule indeks/split'iyle eşleşen final-OOS sonuçlarını kabul eder; finansal oran ve maliyet ilişkileri agregasyon öncesi yeniden doğrulanır.
- `research.WalkForwardRuns` üst sonuç/aggregate kaydını, `research.WalkForwardWindowResults` her pencerenin manifest, split ve tam execution metriklerini tutar.
- Run ve tüm pencere kayıtları tek kısa Serializable SQL transaction'ında yazılır. Aynı run/report idempotent; aynı run/farklı report determinism ihlalidir.
- Her walk-forward penceresi signal ve trend için taze single-use dataset açar ve pencereler sıralı çalışır; böylece stream sahipliği deterministik, bellek kullanımı bounded kalır.
- Train+validation candle'ları yalnız indicator warm-up içindir. OOS değerlendirmesi `ValidationEndExclusive` sınırında `Flat` state ile başlar; pre-OOS strategy position veya ekonomik karar OOS'a taşınamaz.
- Window filtresi stratejiye yalnız `[StartInclusive, OutOfSampleEndExclusive)` aralığını verir, ancak raw dataset tamamlanmış summary ve manifest kanıtı için EOF'ye kadar tüketilir.
- Minimum signal/trend warm-up geçmişi bulunmayan veya OOS'ta sıfır strategy değerlendirmesi üreten pencere fail-closed olur; herhangi bir pencere hatası kısmi birleşik rapor üretmez.
- Strategy action allowlist'i yalnız `Hold`, `EnterLong` ve `ExitToFlat` değerlerinden oluşur; short action yoktur.
- Karardaki signal candle değerlendirme anında kapanmış olmalı; trend candle signal kapanışından daha yeni olamaz.
- Strategy ID/version ve makine-okunur reason code her kararda taşınır. Kesin entry/exit algoritması backtest kararı alınmadan execution'a bağlanmaz.

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

### Paper execution gereksinimleri

- Paper fill aynı policy, order ve market snapshot girdilerinde deterministiktir.
- Minimum latency dolmadan ve limit fiyat koşulu sağlanmadan fill üretilmez.
- Buy ask, sell bid referansını kullanır; slippage yönü kullanıcı aleyhine uygulanır.
- Fill miktarı görünür likidite ve maksimum katılım oranıyla sınırlanır; partial fill desteklenir.
- Quote komisyonu gerçekleşen fiyat ve miktar üzerinden hesaplanır.
- Paper değerlendirmesi order ve reservation durumunu `AsNoTracking` ile okur; fill transaction'ı aggregate'leri Serializable transaction içinde yeniden yükler. Böylece transaction öncesi izlenen eski entity durumu kullanılmaz.
- Piyasa olay kimliğinden türetilen execution kimliği, application ve veritabanı idempotency kapılarında aynı olayın tekrar settle edilmesini engeller.
- Market snapshot değerlendirmesi transaction dışında, order/reservation/balance/position/execution/audit/outbox yazımı ise tek kısa transaction içinde yapılır.
- Hosted worker her turda yeni async DI scope oluşturur; scoped persistence bağımlılıkları singleton worker alanında tutulmaz.
- Worker bütün async çağrılara host cancellation token'ını taşır, beklenen kapanış iptalini normal sonlandırır ve tur hatasını loglayıp bounded polling aralığından sonra yeniden dener.
- Bir top-of-book olayı yalnız aynı instrument'a ait aktif ve kalıcı rezervasyonu bulunan order'lara fan-out edilir; aynı olay/order çifti deterministik execution ID ile idempotenttir.
- Bu ilk model top-of-book seviyesindedir; cumulative depth ve queue position sonraki dilimdir.

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

`.github/workflows/ci.yml`, pull request ve `main` push'larında .NET SDK sürümünü `global.json` üzerinden kurar; format, Release build, ağsız test, EF pending-model/idempotent migration script, NuGet vulnerability ve repository policy kapılarını çalıştırır. Action bağımlılıkları doğrulanmış release commit SHA'larına pinlenir ve workflow yalnız `contents: read` izni taşır.

Gerçek OKX connectivity ve SQL Server transaction testleri dış bağımlılık gerektirdiğinden opt-in kalır; ayrı kontrollü integration job/service kurulmadan normal CI bunları production kanıtı saymaz.
