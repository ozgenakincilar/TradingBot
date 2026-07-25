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
