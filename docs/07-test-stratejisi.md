# Test Stratejisi

**Durum:** Kabul edildi

## 1. Hedef

Testler yalnızca kod satırlarını değil, finansal invariants, hata toparlama ve canlı davranışla backtest arasındaki tutarlılığı kanıtlar.

## 2. Test katmanları

| Katman | Kapsam | Dış bağımlılık |
|---|---|---|
| Unit | Value object, aggregate, risk formülleri, state machine | Yok |
| Property-based | Yuvarlama, para matematiği, transition invariants | Yok |
| Component | Use case + fake port/persistence | Process içi |
| Contract | Exchange payload ve port uyumluluğu | Recorded/fake server |
| Integration | Veritabanı, HTTP, WebSocket, migration | Container/local dependency |
| End-to-end | Market event → risk → paper fill → portfolio | Kontrollü ortam |
| Testnet | Gerçek borsa protokolü | Testnet |
| Chaos/recovery | Disconnect, timeout, duplicate, restart | Kontrollü fault injection |

## 3. Kritik senaryolar

- Tick/lot floor rounding ve min notional sınırları.
- Komisyon sonrası PnL.
- Partial fill ve cancel/fill yarışı.
- Duplicate/out-of-order market ve user events.
- WebSocket gap ve REST onarımı.
- POST timeout sonrası unknown order reconciliation.
- Restart sırasında açık order/position reconstruction.
- Stale data ve clock drift nedeniyle trading halt.
- Daily loss, exposure ve kill switch kontrolleri.
- Futures/margin instrument veya endpoint yapılandırmasının fail-fast reddedilmesi.
- Sell quantity'nin kullanılabilir Spot bakiyeyi aşamaması ve pozisyonun negatif olamaması.
- Getiri hedefinin risk limitlerini veya emir miktarını değiştirmediğinin doğrulanması.
- Graceful shutdown sırasında yeni iş kabul edilmemesi.
- Secret değerinin hiçbir log/exception içinde görünmemesi.

## 4. Backtest doğruluğu

- Look-ahead bias yasaktır; yalnızca olay anında bilinen veri kullanılır.
- Closed candle semantiği live ile aynıdır.
- Komisyon, spread, slippage, latency ve fill olasılığı modellenir.
- Parametre optimizasyonu train/validation/out-of-sample ayrımı kullanır.
- Walk-forward ve farklı market regime testleri uygulanır.
- Sonuçlar strategy version, data version, seed ve config hash ile tekrar üretilebilir olmalıdır.

## 5. Determinizm

- `TimeProvider` fake ile kontrol edilir.
- Random seed kaydedilir.
- Recorded exchange payload’ları immutable fixture’dır.
- Paralel testler ortak mutable state paylaşmaz.
- Floating point kullanıldığında tolerans ve NaN/Infinity davranışı açıktır.

## 6. CI kabul kapıları

- Release build: 0 warning, 0 error.
- Tüm unit/component testleri başarılı.
- Değişen adaptörde contract testleri başarılı.
- Format/analyzer başarılı.
- Vulnerability ve secret scan kritik bulgu içermiyor.
- Migration doğrulaması başarılı.
- Domain coverage hedefi başlangıçta en az %80 branch; oran tek başına kalite ölçüsü değildir.

## 7. Production öncesi aşamalar

1. Offline replay.
2. Paper trading.
3. Exchange testnet.
4. Shadow mode (sinyal var, emir yok).
5. Çok düşük limitli canary live.
6. Kontrollü limit artışı.

Her aşama için en az çalışma süresi ve başarı metriği ürün kararı olarak yazılmadan sonraki aşamaya geçilmez.
