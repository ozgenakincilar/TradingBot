# Operasyon ve Gözlemlenebilirlik

**Durum:** Taslak

## 1. Ortamlar

- Development: fake/paper, local secret.
- Test: otomatik entegrasyon testleri.
- Testnet: borsa test kimlik bilgileri.
- Production: live key, ayrı hesap/izin ve sıkı limitler.

Credential ve endpoint’ler ortamlar arasında paylaşılmaz.

## 2. Deployment ilkeleri

- Immutable artifact/container.
- Non-root process ve read-only filesystem (gerekli volume hariç).
- Resource CPU/memory limitleri.
- Health ve startup probe.
- Açık pozisyon varken deployment varsayılan olarak engellenir.
- Migration ayrı, gözlemlenebilir ve geri dönüş planlı adımdır.
- Rolling update ancak execution sahipliği/leader election tasarlandıysa kullanılır; ilk sürüm single active instance’tır.

## 3. Telemetry

### Log

- JSON structured logging.
- Alanlar: timestamp UTC, level, event ID, correlation ID, symbol, strategy ID, order ID (secret değil), environment.
- API key, signature, auth header ve kişisel veri loglanmaz.
- Sampling execution/audit olaylarını düşüremez.

### Metric

- Market event lag ve sequence gap count.
- WebSocket reconnect count/duration.
- REST latency/error/rate-limit usage.
- Strategy evaluation duration ve signal count.
- Risk approve/reject/resize count.
- Order submit/ack/fill/cancel latency.
- Unknown order ve reconciliation mismatch count.
- Position exposure, realized/unrealized PnL ve drawdown.
- Queue depth/drop count, CPU, memory, GC ve thread pool.

Yüksek cardinality değerler metric label yapılmaz.

### Trace

Market input → strategy → risk → order → fill zinciri correlation ile izlenir. Trace içinde secret ve tam signed query bulunmaz.

## 4. Sağlık modeli

- `/health/live`: Process event loop yanıtlıyor.
- `/health/ready`: Config geçerli, market data fresh, zorunlu dependency erişilebilir.
- `/health/startup`: İlk snapshot/reconciliation tamamlandı.

Health endpoint ayrıntıları anonim dış erişime açılmaz.

## 5. Alarm seviyeleri

| Seviye | Örnek | Davranış |
|---|---|---|
| Info | Planlı reconnect | Dashboard/log |
| Warning | Artan latency, tek gap | Throttled bildirim |
| Critical | Unknown order, reconciliation mismatch, risk breach | Trading pause + acil bildirim |
| Emergency | Auth compromise, kontrolsüz exposure | Kill policy + insan müdahalesi |

Alarm fırtınası deduplication, throttling ve batching ile engellenir.

## 6. Graceful shutdown

1. Readiness false yapılır.
2. Yeni signal/intent kabulü durur.
3. In-flight application işleri sınır süreyle beklenir.
4. Yerel açık emir/pozisyon snapshot kaydedilir.
5. Politikaya göre geçici giriş emirleri iptal edilir; koruyucu server-side emirler korunur.
6. Stream ve persistence temiz kapanır.

Shutdown otomatik olarak pozisyon kapatmaz; bu ayrı ve açık bir risk politikasıdır.

## 7. Runbook özeti

### Unknown order

- Yeni aynı yön emrini engelle.
- ClientOrderId ile REST sorgula.
- User stream ve trade history karşılaştır.
- Sonucu kesinleştirmeden retry etme.

### Market data stale/gap

- İlgili sembolde trading pause.
- Stream reconnect + snapshot gap fill.
- Sequence doğrulanınca readiness geri açılır.

### Reconciliation mismatch

- Yeni exposure engellenir.
- Borsa snapshot gerçeğin kaynağı kabul edilir.
- Audit diff kaydedilir ve operatör bilgilendirilir.

### Secret compromise

- Trading durdurulur.
- Key borsada revoke edilir.
- Yeni key minimum izinle oluşturulur.
- Log/audit ile etki analizi yapılır.
