# Yol Haritası

**Durum:** Taslak

Her aşama bir öncekinin kabul ölçütleri tamamlanmadan başlamaz.

## Aşama 0 — Kararlar ve iskelet

- [x] .NET 10 çözüm iskeleti.
- [x] Clean Architecture + DDD kararı.
- [x] Başlangıç dokümantasyon paketi.
- [ ] İlk borsa ve Spot/Futures kararı.
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
- RiskProfile ve zorunlu limitler.
- Portfolio/PnL hesapları.
- Deterministik paper execution/fill modeli.
- [x] Order ve Instrument için ilk unit testler.
- [ ] Property-based testler ve genişletilmiş finansal sınır testleri.

**Çıkış ölçütü:** Kritik invariants otomatik testlerle kanıtlanmış; gerçek ağ çağrısı yok.

## Aşama 2 — Market data ve persistence

- Exchange metadata/REST adaptörü.
- WebSocket stream, heartbeat, sequence/gap fill.
- Candle aggregation ve warm-up.
- Veritabanı migration, repository ve audit.
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
