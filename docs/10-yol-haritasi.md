# Yol Haritası

**Durum:** Taslak

Her aşama bir öncekinin kabul ölçütleri tamamlanmadan başlamaz.

## Aşama 0 — Kararlar ve iskelet

- [x] .NET 10 çözüm iskeleti.
- [x] Clean Architecture + DDD kararı.
- [x] Başlangıç dokümantasyon paketi.
- [x] Ürün türü kararı: yalnızca kaldıraçsız Spot.
- [ ] İlk Spot borsası kararı.
- [ ] İlk strateji, timeframe ve ürün kapsamı.
- [x] Persistence teknolojisi ADR’si: Microsoft SQL Server.
- [ ] Telemetry teknoloji ADR’si.
- [x] İlk Domain test projesi ve kritik invariant testleri.
- [ ] Application/architecture/integration test projeleri.
- [ ] CI kalite kapıları.

**Çıkış ölçütü:** Açık ürün kararları kapanmış, solution sınır testleri ve CI çalışıyor.

## Aşama 1 — Domain ve paper çekirdek

- [x] Instrument filtrelerinin Domain modeli ve temel value object’ler.
- [x] İlk Order aggregate/state machine uygulaması.
- [x] İlk RiskProfile, position sizing, günlük kayıp, exposure, açık emir ve kill-switch limitleri.
- [x] İlk Spot AssetBalance/Position modeli, rezervasyon, komisyonlu ortalama maliyet ve PnL hesapları.
- [ ] Portfolio persistence, balance/order reconciliation, halt ve iki temiz snapshot+operatör onaylı recovery tamamlandı; kontrollü state correction ve çoklu varlık projection'ları kaldı.
- [x] İlk deterministik paper execution/fill modeli: latency, top-of-book, limit, slippage, komisyon ve partial liquidity.
- [x] Market snapshot → deterministik paper fill → atomik SQL settlement application pipeline'ı ve olay idempotency'si.
- [x] Aktif emir keşfi yapan, scoped persistence kullanan ve cancellation-aware hosted paper market-event worker'ı.
- [x] Order ve Instrument için ilk unit testler.
- [ ] Property-based testler ve genişletilmiş finansal sınır testleri.

**Çıkış ölçütü:** Kritik invariants otomatik testlerle kanıtlanmış; gerçek ağ çağrısı yok.

## Aşama 2 — Market data ve persistence

- [x] EF Core SQL Server provider, DbContext ve repository-local `dotnet-ef` aracı.
- [x] İlk versioned migration ve `execution`, `risk`, `operations` şemaları.
- [x] Orders, RiskDecisions, AuditEvents ve Transactional Outbox tablo temeli.
- [x] Order/RiskDecision/Audit/Outbox repository'leri, retry-aware Unit of Work ve ilk atomik application use case'i.
- [x] Borsa-bağımsız market-data sequence/timestamp integrity state machine ve recovery cursor invariant'ları.
- [x] Integrity-aware market snapshot application servisi: initial/gap recovery, duplicate suppression ve freshness gate.
- [x] Bounded market-event buffer ve atomik snapshot/replay hizalama algoritması.
- [ ] Portfolio repository, hosted paper fill pipeline, fill/reservation, idempotent account reconciliation ve kontrollü halt recovery tamamlandı; gerçek WebSocket/REST adapter bağlantısı, market-data repository, user-stream/trade-history reconciliation ve state correction kaldı.
- Exchange metadata/REST adaptörü.
- WebSocket stream, heartbeat, sequence/gap fill.
- Candle aggregation ve warm-up.
- Genişletilmiş audit/outbox dispatcher ve retention işleri.
- Readiness/startup health.

**Çıkış ölçütü:** Uzun süreli paper çalışmada gap onarımı, restart ve veri bütünlüğü doğrulanmış.

## Aşama 3 — Strateji ve backtest

- İlk strateji sözleşmesi ve sürümleme.
- Closed candle sinyal akışı.
- Tarihsel streaming reader.
- Komisyon/slippage/latency fill modeli.
- Walk-forward ve out-of-sample raporu.

**Çıkış ölçütü:** Aynı event seti aynı kararları üretiyor; look-ahead testleri başarılı.

## Aşama 4 — Testnet execution

- Signed REST ve user data stream.
- Idempotent submit/cancel/amend.
- Partial fill ve reconciliation.
- Server-side protective stop.
- Rate limit ve clock sync.
- Chaos/recovery testleri.

**Çıkış ölçütü:** Timeout, reconnect, restart ve manuel müdahale senaryolarında tutarlı state.

## Aşama 5 — Operasyonel hazırlık

- Structured log, OpenTelemetry metric/trace.
- Dashboard ve throttled dual-channel alarm.
- Kill switch ve runbook tatbikatları.
- Container hardening, backup/restore ve deployment guard.
- Secret rotation ve incident drill.

**Çıkış ölçütü:** Live readiness kontrol listesinin tamamı kanıtlarla kapalı.

## Aşama 6 — Kontrollü live canary

- Açık operatör onayı.
- Minimum notional ve en düşük risk limitleri.
- Tek instrument ve sınırlı çalışma penceresi.
- Günlük insan incelemesi ve otomatik halt koşulları.
- Kanıta göre kademeli limit artışı veya paper moda dönüş.

**Çıkış ölçütü:** Süre ve başarı kriterleri ayrıca kabul edilmeden ölçek artırılmaz.

## Teknik borç politikası

- Güvenlik, finansal doğruluk ve reconciliation borcu sonraki aşamaya taşınmaz.
- Geçici kararlar issue/ADR ile son kullanma ölçütü taşır.
- Her aşamada dokümanlar kodla birlikte güncellenir.
