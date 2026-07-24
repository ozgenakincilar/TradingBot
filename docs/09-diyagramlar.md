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
