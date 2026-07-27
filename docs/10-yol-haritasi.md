# Yol Haritası

**Durum:** Taslak

Her aşama bir öncekinin kabul ölçütleri tamamlanmadan başlamaz.

## Aşama 0 — Kararlar ve iskelet

- [x] .NET 10 çözüm iskeleti.
- [x] Clean Architecture + DDD kararı.
- [x] Başlangıç dokümantasyon paketi.
- [x] Ürün türü kararı: yalnızca kaldıraçsız Spot.
- [x] İlk Spot borsası kararı: OKX TR V5 Spot API.
- [x] İlk strateji zarfı: BTC-USDT long/flat, 15m sinyal, 1H EMA200 trend, 200 candle warm-up ve sürümleme.
- [x] Persistence teknolojisi ADR’si: Microsoft SQL Server.
- [ ] Telemetry teknoloji ADR’si.
- [x] İlk Domain test projesi ve kritik invariant testleri.
- [ ] Application/architecture/integration test projeleri.
- [x] GitHub Actions CI: format, Release build, ağsız test, EF migration drift/script, NuGet vulnerability ve repository policy kapıları.

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
- [x] İlk OKX TR public REST order-book recovery adapter'ı ve recorded contract testleri.
- [x] OKX public `books5` TLS WebSocket client, heartbeat, `prevSeqId` continuity ve gerçek connectivity smoke testi.
- [x] Hosted OKX stream supervisor: bounded snapshot/replay session, execution pump ve reconnect backoff+jitter.
- [x] OKX public Spot instrument metadata adapter'ı, `live`/filtre startup kapısı ve instrument+market-data readiness endpoint'i.
- [x] Borsa-bağımsız UTC timeframe/closed-candle modeli, fail-closed sequence guard ve bounded gap-recovery application portu.
- [x] OKX V5 kapalı candle history adapter'ı, UTC bar allowlist'i ve recorded/gerçek ağ contract testleri.
- [x] Borsa-bağımsız bounded closed-candle warm-up use case'i ve fail-closed lookback doğrulaması.
- [ ] Portfolio repository, hosted paper fill pipeline, fill/reservation, idempotent account reconciliation ve kontrollü halt recovery tamamlandı; gerçek WebSocket/REST adapter bağlantısı, market-data repository, user-stream/trade-history reconciliation ve state correction kaldı.
- [x] Exchange metadata/REST adaptörü.
- WebSocket stream, heartbeat, sequence/gap fill.
- [x] Candle aggregation ve warm-up: exchange-aggregated `15m/1H` business WebSocket, dual startup/reconnect anchor, bounded buffer, closed-only parser ve REST gap recovery tamamlandı.
- Genişletilmiş audit/outbox dispatcher ve retention işleri.
- [ ] Readiness/startup health: instrument+candle-history+market-data readiness tamamlandı; SQL/reconciliation dependency ve ayrı startup probe kaldı.

**Çıkış ölçütü:** Uzun süreli paper çalışmada gap onarımı, restart ve veri bütünlüğü doğrulanmış.

## Aşama 3 — Strateji ve backtest

- [x] İlk strateji sözleşmesi ve sürümleme zarfı; exact entry/exit parametreleri backtest kararı bekliyor.
- [x] Warm-up ve canlı candle'ları birleştiren bounded, fail-closed seri store'u.
- [x] Deterministik decimal EMA(200) ve `1H close > EMA` long trend filtresi; execution bağlantısı yok.
- [x] EMA20 cross entry/exit, `%2` FOMO guard ve versioned strategy decision motoru.
- [x] Bounded historical streaming decision replay; future trend isolation ve gap fail-closed.
- [x] Next-open fill, fee/spread/slippage/latency, önceki-candle likidite proxy'si ve PnL/performance raporu.
- [x] Canonical streaming CSV reader, dataset SHA-256, chronological train/validation/OOS kilidi ve reproducible run manifest.
- [x] Deterministik rolling/expanding walk-forward pencere üreticisi ve çakışmasız OOS zaman politikası.
- [x] Schedule/run/report SHA-256 kimliği, normalize SQL result persistence ve çoklu OOS birleşik rapor modeli.
- [x] Streaming tarihsel walk-forward orchestration, pencere başına taze dataset, OOS-flat state izolasyonu ve final manifest/report üretimi.
- [x] OKX tarihsel candle'larını bounded 100'lü sayfalarla atomik canonical CSV dataset'e aktaran export pipeline'ı.
- [x] Secretsiz public OKX research export CLI ve sıkı UTC/timeframe/path komut sözleşmesi.
- [x] Maliyetli buy-and-hold OOS benchmark, excess getiri, report identity ve SQL persistence.
- [x] Canonical gerçek CSV çifti üzerinde versioned baseline walk-forward raporu çalıştıran secretsiz research CLI.
- [x] Strategy ve benchmark için muhafazakâr tick/lot rounding, minimum quantity/notional kapısı, versioned configuration hash ve legacy kanıt uyumluluğu.
- [x] OKX `books5`/REST için bounded beş seviyeli depth ve paper execution'da participation-sınırlı cumulative VWAP market impact.
- [x] Son kapalı mum volatilitesi/hacmiyle doğrusal olmayan spread/slippage, `%5` toplam katılım ve bounded mum-içi TWAP child-order execution sözleşmesi; legacy v1-v5 hash yolları korundu.
- [x] Buy-and-hold benchmark için strateji simülatörüyle ortak dinamik maliyet, `%5` katılım, quantization ve bounded TWAP çekirdeği.
- Gerçek limit-order queue replay, hidden liquidity ve cancel latency.
- [x] Küratörlü gerçek 2025 BTC-USDT 15m/1H dataset üretimi ve beş pencereli ilk OOS değerlendirmesi; v1 baseline kabul ölçütlerini geçemedi.
- [x] v1 hash/karar uyumluluğunu koruyan, round-trip maliyetinden türetilmiş 30 bps EMA hysteresis v2 araştırma sözleşmesi.
- [x] v1-v2 validation-only karşılaştırma, önceden kayıtlı acceptance evaluator ve fail-closed research CLI.
- [x] Ayrı 2024 development datasetinde v2 train/validation kabul çalışması; v2 pozitif net, benchmark excess ve kârlı pencere eşiklerini geçemediği için reddedildi ve yeni final OOS açılmadı.
- [x] Reddedilmiş v2 için bounded işlem bazlı MFE/MAE, exit-reason ve execution-cost attribution; 2024 validation kanıtı küçük brüt edge'in turnover/maliyet ve geç çıkışla tüketildiğini gösterdi.
- [x] v3 için dört-candle re-entry cooldown, 100/50 bps trailing profit-protection ve ayrı development validation kapılarının sonuç öncesi ön kaydı.
- [x] Ayrı 2023 development train/validation verisinde v2-v3 karşılaştırması; v3 trade ve maliyeti artırıp beş pencerenin tamamını negatif kapattığı için reddedildi, OOS açılmadı.
- [x] v4 için yalnız entry'de `1H ADX(14) >= 25` kullanan trend-kalite filtresi, exact Wilder matematiği ve sekiz acceptance kapısının veri çalıştırmadan önce tasarımı/ön kaydı.
- [x] v4 ADX domain implementasyonu, legacy hash regression testleri ve fail-closed v2-v4 validation CLI.
- [x] Ayrı 2022 historical development train/validation değerlendirmesi; v4 turnover/maliyet kapılarını geçmesine rağmen profit factor, pozitif net, benchmark excess ve kârlı pencere kapılarını geçemediği için reddedildi; holdout açılmadı ve sonuç forward OOS sayılmadı.
- [x] Reddedilmiş v4 için 2022 train/validation işlem bazlı attribution; zarar yalnız maliyet kaynaklı değil, ADX gücünün long yönlü follow-through sağlamaması ve EMA cross-down grubundaki negatif brüt edge ile açıklanıyor.
- [x] v5 için v4 gücünü koruyan entry-only `+DI > -DI` yön doğrulaması, exact DMI matematiği ve sekiz validation kapısının 2021 verisi indirilmeden önce ön kaydı.
- [x] v5 DMI domain/strategy implementasyonu, legacy hash regression testleri ve fail-closed v4-v5 validation CLI.
- [x] Ayrı 2021 historical development train/validation değerlendirmesi; v5 trade/maliyet azaltımı ve drawdown kapılarını geçmesine rağmen profit factor, pozitif net, benchmark excess ve kârlı pencere kapılarını geçemediği için reddedildi; holdout açılmadı ve sonuç forward OOS sayılmadı.
- [x] Reddedilmiş v5 için 2021 train/validation işlem bazlı attribution; yaklaşık brüt edge'in pozitif fakat çok küçük olduğu, 15m EMA cross-down turnover'ı ve execution maliyetinin bu edge'i tükettiği gösterildi.
- [x] v6 için v5 yön/güç kurallarını koruyan `ATR(14) × 0,2` signal EMA bandı, exact Wilder matematiği, legacy hash koruması ve forward acceptance kapılarının ön kaydı.
- [x] v6 ATR period/multiplier için bounded immutable grid, validation-only Profit Factor seçimi, deterministik tie-break ve taze dataset oturumuyla bakir OOS enjeksiyonu; legacy walk-forward yolu korunuyor.
- [x] Kilitli dinamik execution policy, dokuz adaylı ATR grid'i, sekiz acceptance kapısı ve yalnız acceptance reddinde exit `3` üreten v5-v6 forward validation CLI.
- [ ] 2026-07-27 sonrası en az beş kesişmeyen 30 günlük forward pencerenin oluşması ve kilitli v6 acceptance koşusu.
- Pozitif expectancy hipotezli yeni strateji sürümü, ayrı development dataset, kilitli yeni market-regime OOS kabulü ve aylık segmentasyon.

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
