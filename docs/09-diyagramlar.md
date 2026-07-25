# Sistem Diyagramları

**Durum:** Taslak

## 1. Sistem bağlamı

```mermaid
flowchart LR
    Operator[Operatör] -->|Yapılandırma / Kill Switch| Bot[TradingBot]
    Bot <-->|REST + WebSocket| Exchange[Borsa]
    Bot --> Database[(Veritabanı)]
    Bot --> Observability[Log / Metric / Trace]
    Bot --> Notification[Bildirim Kanalları]
    Monitoring[İzleme Sistemi] -->|Health probe| Bot
```

```mermaid
sequenceDiagram
    participant WS as WebSocket Producer
    participant BUF as Bounded Channel (Wait)
    participant REST as REST Snapshot
    participant ALIGN as Replay Aligner
    participant DOWN as Downstream

    WS->>BUF: Buffered events (backpressure)
    REST-->>ALIGN: Snapshot sequence N
    BUF-->>ALIGN: Arrival-ordered buffered events
    ALIGN->>ALIGN: Drop sequence <= N overlap
    ALIGN->>ALIGN: Validate N+1, N+2, ...
    alt Tam seri contiguous
        ALIGN-->>DOWN: Publish validated batch
    else Gap/conflict/time regression
        ALIGN-->>DOWN: Publish nothing
        ALIGN-->>REST: New recovery required
    end
```

## 2. Container/bileşen görünümü

```mermaid
flowchart TB
    subgraph Host
        API[Operations API]
        Workers[Hosted Workers]
        DI[Composition Root]
    end

    subgraph Application
        UseCases[Use Cases]
        Ports[Ports]
    end

    subgraph Domain
        Market[Market Data Model]
        Strategy[Strategy]
        Risk[Risk]
        Execution[Order Aggregate]
        Portfolio[Portfolio]
    end

    subgraph Infrastructure
        ExchangeAdapter[Exchange Adapter]
        Persistence[Persistence]
        Secrets[Secret Provider]
        Telemetry[Telemetry]
    end

    API --> UseCases
    Workers --> UseCases
    UseCases --> Domain
    UseCases --> Ports
    ExchangeAdapter -.implements.-> Ports
    Persistence -.implements.-> Ports
    Secrets -.implements.-> Ports
    DI --> Infrastructure
```

## 3. Market data akışı

```mermaid
sequenceDiagram
    participant W as MarketDataWorker
    participant R as Exchange REST
    participant S as Exchange WebSocket
    participant Q as Bounded Channel
    participant M as Market Data Module
    participant T as Strategy

    W->>R: Instrument metadata + snapshot
    W->>S: Subscribe
    S-->>W: Buffered events
    W->>W: Sequence alignment
    W->>Q: Normalized events
    Q->>M: Validate sequence/time
    M->>T: Closed candle / valid snapshot
    alt Gap detected
        M-->>T: Pause symbol
        M->>R: Fetch recovery snapshot
        M->>M: Rebuild and validate
        M-->>T: Resume symbol
    end
```

### Market-data integrity state machine

```mermaid
stateDiagram-v2
    [*] --> NotReady
    NotReady --> Ready: Geçerli REST recovery snapshot
    NotReady --> NotReady: Stream event / eski recovery
    Ready --> Ready: Beklenen next sequence
    Ready --> Ready: Exact duplicate / geç eski event yok sayılır
    Ready --> NotReady: Sequence gap
    Ready --> NotReady: Aynı sequence + farklı event ID
    Ready --> NotReady: Event/receive time gerilemesi
    NotReady --> Ready: Yeni ve doğrulanmış recovery cursor
```

Guard son güvenilir cursor'u gap sırasında ilerletmez. Bu sayede recovery adapter'ı hangi sequence'den itibaren snapshot/replay gerektiğini kesin olarak bilir.

```mermaid
flowchart LR
    STREAM[GetTopOfBookAsync] --> SERVICE[MarketSnapshotService]
    SERVICE --> GUARD[Instrument Integrity Guard]
    GUARD -- Accepted + fresh --> EXEC[Paper execution cycle]
    GUARD -- Duplicate / out-of-order / stale --> DROP[Withhold event]
    GUARD -- Gap / conflict / time regression --> REST[GetRecoverySnapshotAsync]
    REST --> GUARD
    GUARD -- Recovery applied + fresh --> EXEC
    GUARD -- Recovery rejected --> HALT[Fail closed]
```

## 4. Emir yaşam döngüsü

```mermaid
sequenceDiagram
    participant MD as Market Data
    participant ST as Strategy
    participant RK as Risk
    participant EX as Execution
    participant DB as Database
    participant BX as Exchange

    MD->>ST: Closed candle/event
    ST->>RK: TradeIntent
    RK->>RK: Limits + exposure + freshness
    alt Rejected
        RK-->>ST: Rejected(reason)
    else Approved/Resized
        RK->>EX: Approved intent
        EX->>DB: Persist Draft + ClientOrderId
        EX->>BX: Submit order
        alt Accepted
            BX-->>EX: ExchangeOrderId/status
            EX->>DB: Persist Open
            BX-->>EX: Fill events
            EX->>DB: Persist fills/state
        else Timeout / ambiguous
            EX->>DB: Persist Unknown
            EX->>BX: Query by ClientOrderId
            BX-->>EX: Authoritative state
            EX->>DB: Reconcile
        end
    end
```

## 5. Deployment

```mermaid
flowchart LR
    subgraph Production Network
        Probe[External Health Monitor]
        subgraph Bot Host
            App[TradingBot Process<br/>single active]
            Volume[(Encrypted state/log volume)]
        end
        Metrics[Metrics/Logs Backend]
        Vault[Secret Vault]
    end

    Exchange[Borsa API]
    Notify[Primary + Backup Alert]

    Probe --> App
    App --> Vault
    App <--> Exchange
    App --> Volume
    App --> Metrics
    App --> Notify
```

## 6. ACID ve dış borsa sınırı

```mermaid
sequenceDiagram
    participant APP as Application
    participant DB as SQL Server
    participant BX as Exchange
    participant REC as Reconciliation

    APP->>DB: BEGIN
    APP->>DB: Order=Submitting + Audit + Outbox
    APP->>DB: COMMIT
    Note over DB,BX: SQL transaction dış çağrı boyunca açık değildir
    APP->>BX: Submit(ClientOrderId)
    alt Kesin cevap
        BX-->>APP: Accepted / Rejected
        APP->>DB: Kısa ACID transaction ile sonucu yaz
    else Timeout veya network partition
        APP->>DB: Order=Unknown + trading block
        APP->>REC: Reconciliation talebi
        REC->>BX: ClientOrderId ile sorgula
        BX-->>REC: Yetkili durum
        REC->>DB: Kısa ACID transaction ile düzelt
    end
```

## 7. Tutarlılık bölgeleri

```mermaid
flowchart LR
    Exchange[Borsa] <-->|Ağ bölünebilir| Boundary[Adapter / Reconciliation]
    Boundary --> Core[Execution + Risk + Portfolio<br/>CP eğilimli]
    Core --> SQL[(SQL Server<br/>yerel ACID)]
    SQL --> Outbox[Transactional Outbox]
    Outbox --> Projections[Dashboard / Analytics<br/>eventual consistency]
```

## 8. Bağımlılık yönü

```mermaid
flowchart LR
    Host --> Application
    Host --> Infrastructure
    Infrastructure --> Application
    Infrastructure --> Domain
    Application --> Domain
```

Okların hiçbiri dış katmandan içeri doğru ters çevrilemez; Domain bağımsız kalır.

## 9. Tam gerçekleşen Spot fill persistence akışı

```mermaid
sequenceDiagram
    participant EX as Paper Execution
    participant APP as PersistCompletedSpotFill
    participant PF as Portfolio Domain
    participant DB as SQL Server
    participant OB as Outbox Dispatcher

    EX->>APP: Completed fill (ExchangeExecutionId)
    APP->>DB: BEGIN SERIALIZABLE
    APP->>DB: Execution ID mevcut mu?
    alt Duplicate
        DB-->>APP: Mevcut
        APP->>DB: COMMIT (değişiklik yok)
    else Yeni fill
        DB-->>APP: Balance + Position snapshot
        APP->>PF: Reserve + settle + fee-adjusted PnL
        PF-->>APP: Yeni tutarlı durum
        APP->>DB: Balance + Position + Execution + Audit + Outbox
        APP->>DB: COMMIT
        DB-->>OB: Commit sonrası yayınlanabilir mesaj
    end
```

Bu kısa yol tamamen gerçekleşen paper fill içindir. Açık ve parçalı emirler aşağıdaki kalıcı rezervasyon yaşam döngüsünü kullanır.

## 10. Partial fill ve kalıcı rezervasyon yaşam döngüsü

```mermaid
sequenceDiagram
    participant EX as Paper/Exchange Execution
    participant APP as Reservation Use Cases
    participant ORD as Order Aggregate
    participant PF as Portfolio Domain
    participant DB as SQL Server

    APP->>DB: BEGIN SERIALIZABLE
    APP->>PF: Reserve buy quote / sell base
    APP->>DB: OrderReservation + Balance/Position + Audit + Outbox
    APP->>DB: COMMIT
    loop Her benzersiz partial fill
        EX->>APP: Fill(ExecutionId, quantity, price, fee)
        APP->>DB: BEGIN SERIALIZABLE + duplicate kontrolü
        APP->>ORD: ApplyFill
        APP->>PF: Consume only actual fill + fee
        APP->>DB: Order + Reservation + Portfolio + Execution + Audit + Outbox
        APP->>DB: COMMIT
    end
    alt Fill kalan miktarı tamamlar
        APP->>PF: Fiyat/fee tahmin fazlasını serbest bırak
        APP->>ORD: Filled
    else Cancel onayı önce kesinleşir
        APP->>PF: Yalnız RemainingReserved değerini serbest bırak
        APP->>ORD: Cancelled
    end
```

Serializable transaction ve `rowversion`, aynı order üzerindeki fill/cancel yarışında lost update'i engeller. Terminal reservation'a gelen geç olay bakiye veya PnL oluşturamaz.

## 11. Spot account reconciliation ve trading halt

```mermaid
sequenceDiagram
    participant EX as Exchange Account API
    participant REC as ReconcileSpotAccount
    participant DB as SQL Server
    participant ORD as Order Persistence Gate

    EX-->>REC: SnapshotId + canTrade + balances + open orders
    REC->>DB: BEGIN SERIALIZABLE
    REC->>DB: SnapshotId/hash duplicate kontrolü
    REC->>DB: Yerel balances + active orders
    REC->>REC: Deterministik karşılaştırma
    alt Fark veya canTrade=false
        REC->>DB: ReconciliationRun + TradingSafetyState=Halted
        REC->>DB: Audit + Outbox + COMMIT
        ORD->>DB: Yeni order için safety state sorgusu
        DB-->>ORD: Halted
        ORD-->>ORD: Yeni exposure reddedilir
    else Tutarlı snapshot
        REC->>DB: ReconciliationRun + Audit + Outbox + COMMIT
        Note over REC,DB: Önceden aktif halt otomatik kaldırılmaz
    end
```

## 12. Kontrollü trading safety recovery

```mermaid
sequenceDiagram
    participant OP as Yetkili Operatör
    participant REC as Reconciliation
    participant SAFE as RecoverTradingSafety
    participant DB as SQL Server
    participant ORD as Order Persistence Gate

    REC->>DB: Clean snapshot #1 (halt sonrasında)
    REC->>DB: Clean snapshot #2 (halt sonrasında)
    OP->>SAFE: RecoveryId + OperatorId + Reason
    SAFE->>DB: BEGIN SERIALIZABLE
    SAFE->>DB: Halt state + son 2 run + duplicate RecoveryId
    alt Kanıt eksik veya snapshot kirli
        SAFE-->>OP: Recovery reddedildi
    else İki snapshot tutarlı ve canTrade=true
        SAFE->>DB: SafetyState=Ready + Recovery + Audit + Outbox
        SAFE->>DB: COMMIT
        ORD->>DB: Yeni risk onayının zamanı safety transition'dan sonra mı?
        alt Eski risk onayı
            ORD-->>ORD: Reddet; yeniden risk değerlendirmesi gerekli
        else Yeni risk onayı
            ORD->>DB: Atomik order persistence
        end
    end
```

## 13. Deterministik paper execution

```mermaid
flowchart TD
    A[Order + Remaining Quantity] --> D{Minimum latency doldu mu?}
    M[Top-of-book Bid/Ask + Quantity] --> D
    P[Commission + Slippage + Participation Policy] --> D
    D -- Hayır --> W1[WaitingForLatency]
    D -- Evet --> L{Slippage-adjusted fiyat limit koşulunda mı?}
    L -- Hayır --> W2[WaitingForLimitPrice]
    L -- Evet --> Q[Fill Qty = min kalan, görünür likidite x katılım]
    Q --> Z{Fill qty pozitif mi?}
    Z -- Hayır --> W3[WaitingForLiquidity]
    Z -- Evet --> F[Deterministik Partial/Full Fill + Quote Fee]
```

## 14. Uçtan uca paper fill persistence pipeline

```mermaid
sequenceDiagram
    participant MD as Market Event
    participant APP as ProcessPaperOrderSnapshot
    participant READ as PaperOrderReader
    participant ENG as PaperExecutionEngine
    participant FILL as ApplySpotOrderFill
    participant DB as SQL Server

    MD->>APP: OrderId + MarketEventId + TopOfBook
    APP->>READ: Order + active reservation (AsNoTracking)
    READ->>DB: Salt okunur snapshot
    APP->>ENG: Evaluate(snapshot, policy)
    alt Waiting
        ENG-->>APP: Latency / limit / liquidity bekleniyor
    else Fill
        ENG-->>APP: Deterministik partial/full fill
        APP->>FILL: PAPER-{OrderId}-{EventHash}
        FILL->>DB: BEGIN Serializable
        FILL->>DB: Aggregate'leri yeniden yükle + idempotency kontrolü
        alt Execution zaten var
            FILL->>DB: ROLLBACK/etkisiz sonuç
            FILL-->>APP: FillAlreadyApplied
        else Yeni execution
            FILL->>DB: Order + Reservation + Balance + Position
            FILL->>DB: Execution + Audit + Outbox
            FILL->>DB: COMMIT
            FILL-->>APP: FillApplied
        end
    end
```

## 15. Hosted paper market-event döngüsü

```mermaid
flowchart TD
    HOST[Generic Host / TradingWorker] --> MD[IMarketDataClient: Top-of-book event]
    MD --> SCOPE[Her turda yeni async DI scope]
    SCOPE --> CYCLE[ProcessPaperMarketEvent]
    CYCLE --> QUERY[Instrument + active reservation order sorgusu]
    QUERY --> LOOP{Her aktif order}
    LOOP --> PIPE[ProcessPaperOrderSnapshot]
    PIPE --> TX[Bağımsız Serializable settlement]
    TX --> LOOP
    LOOP --> DELAY[Bounded polling delay]
    DELAY --> HOST
    HOST -. CancellationToken .-> STOP[Graceful cycle stop]
    CYCLE -. Hata .-> LOG[Structured error log]
    LOG --> DELAY
```

Worker singleton olsa da scoped persistence nesnesi taşımaz. Market event fan-out sıralıdır; bu ilk sürümde aynı order üzerinde paralel settlement yarışı üretilmez.

## 16. OKX public books5 WebSocket taşıması

```mermaid
sequenceDiagram
    participant C as OkxSpotMarketStreamClient
    participant WS as OKX WSS / books5
    participant P as Books5 Parser
    participant G as Integrity Guard

    C->>WS: TLS connect + subscribe(BASE-QUOTE)
    WS-->>C: Subscribe acknowledgement
    C-->>C: Control mesajı; yayınlama
    WS-->>C: Fragmented books5 snapshot
    C->>C: ArrayPool buffer + 64 KiB limit
    C->>P: Complete UTF-8 JSON
    P->>G: seqId + prevSeqId + event/receive time
    alt 20 saniye veri yok
        C->>WS: ping
        WS-->>C: pong
    end
    alt İkinci heartbeat timeout
        C-->>C: Connection failure; supervisor yeniden bağlar
    end
```

## 17. OKX hosted recovery ve reconnect supervisor

```mermaid
flowchart TD
    HOST[OkxTradingWorker] --> SESSION[MarketDataStreamSession]
    SESSION --> WS[books5 WebSocket producer]
    WS --> BUF[Bounded buffer]
    SESSION --> REST[REST order-book snapshot]
    REST --> ALIGN[Cross-source freshness check]
    BUF --> ALIGN[İlk books5 full snapshot sequence anchor]
    ALIGN --> GUARD[Full snapshot monotonicity + freshness]
    GUARD --> SAMPLE[Polling aralığında execution sample]
    SAMPLE --> SCOPE[Yeni DI scope]
    SCOPE --> PAPER[ProcessPaperMarketEvent]
    WS -. disconnect/gap/timeout .-> FAIL[Session fail-closed]
    FAIL --> BACKOFF[Exponential backoff + jitter]
    BACKOFF --> SESSION
```

## 18. OKX Spot başlangıç ve readiness kapısı

```mermaid
flowchart TD
    CFG[TradingOptions: OKX/BASE-QUOTE] --> CAT[OKX public instruments REST]
    CAT --> VALID{SPOT + live + symbol eşleşmesi\npozitif tickSz/lotSz/minSz}
    VALID -- Hayır --> STOP[Host startup fail-fast\nEndpoint erişilemez]
    VALID -- Evet --> IR[Instrument ready]
    IR --> SIGNAL{15m / 200 signal warm-up\nexact + contiguous}
    SIGNAL -- Hayır --> STOP
    SIGNAL -- Evet --> TREND{1H / 200 trend warm-up\nexact + contiguous}
    TREND -- Hayır --> STOP
    TREND -- Evet --> CR[Dual candle history ready]
    CR --> WORKER[OkxTradingWorker başlar]
    WORKER --> WS[books5 WSS + REST recovery]
    WS --> EVENT{İlk doğrulanmış event}
    EVENT -- Hayır --> WAIT[Readiness 503]
    EVENT -- Evet --> READY[Readiness 200]
    READY --> PAPER[Paper execution sample]
    WS -. disconnect/gap/timeout .-> WAIT
```

Bu readiness instrument, kapalı candle geçmişi ve market-data kapılarını temsil eder. SQL Server erişimi ve startup reconciliation ayrı kontroller olarak eklenene kadar production-ready iddiası oluşturmaz.

## 19. Kapalı candle gap recovery

```mermaid
flowchart TD
    LAST[Son kabul edilen kapalı candle] --> EXPECT[Expected open = last close]
    STREAM[Yeni kapalı candle] --> CHECK{Open time expected mı?}
    CHECK -- Evet --> ACCEPT[Sequence accepted]
    CHECK -- Hayır, ileri --> PAUSE[Series not-ready]
    PAUSE --> RANGE[OKX history-candles\nbounded expected..observed close]
    RANGE --> VALID{Tam sayı + UTC boundary +\naynı instrument/timeframe + closed}
    VALID -- Hayır --> CLOSED[Hiçbir kısmi candle yayınlama]
    VALID -- Evet --> ATOMIC[Contiguous recovery atomik uygula]
    ATOMIC --> READY[Series ready]
```

## 20. Kapalı candle warm-up kapısı

```mermaid
flowchart TD
    NOW[Ortak KnownAt UTC] --> SIGNAL[15m / 200 signal range]
    SIGNAL --> SGUARD{Exact + closed + contiguous}
    SGUARD -- Hayır --> REJECT[Host startup fail-closed]
    SGUARD -- Evet --> TREND[1H / 200 trend range]
    TREND --> TGUARD{Exact + closed + contiguous}
    TGUARD -- Hayır --> REJECT
    TGUARD -- Evet --> CANDLE_READY[Signal + Trend CandleHistoryReady]
```

Her seri kendi timeframe sınırını aynı UTC `knownAt` değerinden hesaplar; devam eden açık candle hiçbir aralığa dahil edilmez. Instrument ve iki candle-history kapısı geçtikten sonra stream başlar; ilk doğrulanmış market event gelene kadar genel readiness yine kapalıdır.

## 21. İlk sürümlü strateji zarfı

```mermaid
flowchart LR
    S15[15m contiguous closed candles\nminimum 200] --> SYNC{Timeframe ve kapanış\nuyumlu mu?}
    T1H[1H contiguous closed candles\nminimum 200 + EMA200] --> SYNC
    DEF[btc-usdt-long-flat-baseline\nv1 / OKX:BTC-USDT] --> SYNC
    SYNC -- Açık/future/gap/identity mismatch --> HALT[Karar üretme]
    SYNC -- Geçerli --> EVAL[Deterministik strategy evaluation]
    EVAL --> HOLD[Hold]
    EVAL --> LONG[EnterLong]
    EVAL --> FLAT[ExitToFlat]
    LONG --> RISK[TradeIntent değil\nönce ayrı dönüşüm + Risk]
    FLAT --> RISK
```

Short action sözleşmede bulunmaz. Kesin entry/exit formülü backtest ve out-of-sample kanıtı kabul edilene kadar `EVAL` uygulaması execution'a bağlanmaz.

## 22. Canlı multi-timeframe candle ve gap recovery

```mermaid
flowchart TD
    WORKER[OkxCandleWorker] --> CLOSED[Signal + Trend readiness kapalı]
    CLOSED --> WS[OKX business WSS\ncandle15m + candle1H]
    WS --> BUFFER[Bounded candle buffer\ncapacity 64 / wait]
    CLOSED --> REST[Her timeframe için son kapalı REST anchor]
    REST --> GUARDS[15m ve 1H sequence guard]
    GUARDS --> READY[SessionReady\niki readiness açık]
    BUFFER --> PARSE{confirm=1 ve contract geçerli mi?}
    PARSE -- confirm=0 --> IGNORE[Açık candle'ı yayınlama]
    PARSE -- invalid --> RECONNECT[Readiness kapat + backoff/jitter]
    PARSE -- closed --> OBSERVE{Contiguous mi?}
    OBSERVE -- duplicate/old --> IGNORE
    OBSERVE -- next --> PIPE[Validated closed-candle pipeline]
    OBSERVE -- gap --> FILL[Bounded REST gap recovery]
    FILL -- complete --> PIPE
    FILL -- invalid/oversized --> RECONNECT
    WS -. close/heartbeat timeout .-> RECONNECT
    RECONNECT --> WS
```

Bu akış trade tick'lerinden yerel OHLCV üretmez; ilk sürümde borsanın aggregate candle kanalı anti-corruption adapter'ında normalize edilir. Strateji/economic intent bağlantısı ayrı bir sonraki dilimdir.

## 23. Bounded candle serisi ve deterministik EMA trend filtresi

```mermaid
flowchart TD
    START[Startup veya reconnect] --> WARM[15m + 1H tam warm-up\naynı UTC knownAt]
    WARM --> VALID{Exact, closed ve contiguous mı?}
    VALID -- Hayır --> CLOSED[Readiness kapalı\nkarar üretme]
    VALID -- Evet --> SEED[Timeframe başına bounded store\ncapacity 300]
    LIVE[Validated closed live candle] --> APPEND{Store append}
    SEED --> APPEND
    APPEND -- Duplicate / eski --> IGNORE[Seriyi ilerletme]
    APPEND -- Gap / conflict --> CLOSED
    APPEND -- Contiguous --> TRIM[Append + en eskiyi buda]
    TRIM --> SNAP[Immutable ready snapshot]
    SNAP --> LAST200[Son tam 200 adet 1H candle]
    LAST200 --> EMA[Decimal EMA200\nfirst close seed]
    EMA --> FILTER{Son close EMA üstünde mi?}
    FILTER -- Evet --> ALLOW[Long yönüne izin]
    FILTER -- Hayır --> HOLD[Long izni yok]
    ALLOW --> NOEXEC[Henüz execution'a bağlı değil]
    HOLD --> NOEXEC
```

Aynı son 200 candle penceresi aynı EMA sonucunu üretir. Aylık getiri hedefi ve risk limitleri bu hesaplamaya parametre olarak girmez.

## 24. Deterministik v1 karar replay'i

```mermaid
flowchart TD
    S[Async 15m closed candles] --> MERGE[Kapanış zamanına göre streaming merge]
    T[Async 1H closed candles] --> MERGE
    MERGE --> ORDER{Close time eşit mi?}
    ORDER -- Evet --> TFIRST[1H trend önce]
    ORDER -- Hayır --> KNOWN[Yalnız o anda bilinen candle]
    TFIRST --> WINDOWS[Bounded 200-candle windows]
    KNOWN --> WINDOWS
    WINDOWS --> VALID{Identity + contiguous + warm-up}
    VALID -- Hayır --> FAIL[Replay fail-closed]
    VALID -- Evet --> POS{Sanal state}
    POS -- Flat --> ENTRY[1H close > EMA200\n15m EMA20 cross-up\nbody <= 2%]
    POS -- Long --> EXIT[1H trend loss veya\n15m EMA20 cross-down]
    ENTRY --> DECISION[Versioned StrategyDecision]
    EXIT --> DECISION
    DECISION --> STATE[Flat/Long state update]
    STATE --> NOEXEC[Fill, PnL, intent veya order yok]
```

Gelecekteki trend candle enumerator tarafından görülse bile sinyal kapanışından önce pencereye alınmaz. Aynı veri sırası ve v1 tanımı aynı karar dizisini üretir.
