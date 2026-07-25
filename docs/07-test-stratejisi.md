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
- Paper market/limit fill'in latency, bid/ask, slippage, komisyon ve likidite katılım kurallarına uyması.
- Aynı paper execution girdisinin bit düzeyinde aynı fill sonucunu üretmesi.
- Market snapshot'ın application pipeline üzerinden partial fill üretmesi; order, reservation, balance, position, execution, audit ve outbox kayıtlarının gerçek SQL Server transaction'ında birlikte güncellenmesi.
- Aynı piyasa olayının tekrar işlenmesinde mevcut execution'ın dönmesi ve ikinci ekonomik etkinin oluşmaması.
- Aktif order keşfinin instrument ve aktif reservation ile sınırlandırılması; market-event cycle'ın aynı olayı yeniden gördüğünde SQL Server'da tek execution bırakması.
- Aynı borsa execution ID'sinin tekrar uygulanmasının bakiye, pozisyon ve PnL'yi değiştirmemesi.
- Portfolio snapshot, execution ledger, audit ve outbox'ın gerçek SQL Server transaction'ında birlikte commit edilmesi.
- Partial fill ve cancel/fill yarışı.
- Partial fill sonrasında yalnız kalan rezervasyonun cancel ile açılması; final fill sonrası fiyat iyileşmesi fazlasının iadesi.
- Fill-first ve cancel-first sıralamalarında tek terminal sonucun kazanması; geç kalan olayın ekonomik durumu değiştirmemesi.
- Duplicate/out-of-order market ve user events.
- WebSocket gap ve REST onarımı.
- Başlangıç recovery snapshot'ı olmadan market-data readiness oluşmaması.
- Duplicate/out-of-order event'in cursor'u geri sarmaması; gap, çelişkili sequence ve timestamp regression'ın instrument'ı durdurması.
- Eski recovery snapshot'ın reddedilmesi ve freshness sınırının receive time üzerinden uygulanması.
- Application market snapshot servisinin ilk event'te recovery çağırması, sıralı event'i doğrudan yayınlaması, duplicate'i kesmesi, gap'i snapshot ile onarması ve stale sonucu execution'a vermemesi.
- Bounded market event buffer dolduğunda producer'ın beklemesi ve bekleyen write'ın cancellation ile sonlanması.
- Snapshot overlap temizliği ve contiguous replay; gap/conflict/timestamp regression durumunda hiçbir kısmi event'in yayınlanmaması.
- POST timeout sonrası unknown order reconciliation.
- Exchange/local balance ve aktif order snapshot farkının kalıcı halt oluşturması.
- Duplicate reconciliation snapshot'ın idempotent olması ve aynı ID ile çelişen içeriğin reddedilmesi.
- Reconciliation halt aktifken yeni ekonomik order'ın persistence kapısında reddedilmesi.
- Tek temiz snapshot ile recovery'nin reddedilmesi; iki ardışık temiz snapshot ve operatör kanıtıyla halt'ın açılması.
- Recovery sonrasında eski risk onayının reddedilip yalnız yeni risk kararının kabul edilmesi.
- Duplicate recovery ID'nin idempotent, çelişen recovery içeriğinin hatalı olması.
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
