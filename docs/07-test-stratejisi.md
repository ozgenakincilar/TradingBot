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
- Closed candle'ın UTC timeframe sınırı, kapanış zamanı ve OHLCV invariant'ları; açık candle'ın strategy sözleşmesine girememesi.
- Candle sequence'in recovery öncesi kapalı olması; duplicate/out-of-order davranışı ve gap/çelişkide son güvenilir sınırı ilerletmeden fail-closed kalması.
- Bounded candle REST recovery aralığının eksik, fazla, sırasız, yanlış instrument/timeframe veya açık candle yanıtının tamamını reddetmesi.
- OKX history-candles ters sıra mapping'i, `confirm=0` reddi, exact range, UTC bar allowlist'i ve upstream hata mesajı sanitization contract testleri; opt-in gerçek ağda gecikmeli iki tamamlanmış `1m` candle kontrolü.
- Resmî 100-candle history sayfa sınırının adapter'da ağ çağrısından önce reddi; 205 candle export'un 100/100/5 exact ve contiguous sayfalara ayrılması.
- Canlı 200-candle startup warm-up'ın ortak paged history decorator üzerinden iki exact 100-candle çağrıyla tamamlanması ve kısa ikinci sayfada fail-closed olması.
- Export pacing sırasında cancellation'ın sonraki sayfayı ve artifact completion'ı durdurması; eksik sayfanın final artifact üretmemesi.
- Atomik CSV writer çıktısının mevcut streaming reader ile raw SHA/count/range doğrulanması; stream hatasında target/partial bırakmaması ve var olan hedefi overwrite etmemesi.
- Research CLI'nın yalnız `export-candles`, `15m|1H`, BASE-QUOTE instrument, exact UTC timestamp, source ve output allowlist'ini kabul etmesi; bilinmeyen/duplicate/offset'li argümanı reddetmesi.
- Warm-up'ın açık mevcut candle'ı dışlaması, exact boundary davranışı, cancellation aktarımı ve eksik/kaymış/gap içeren lookback'i bütünüyle reddetmesi.
- OKX startup kapısının aynı `knownAt` ile sıralı `15m/200` signal ve `1H/200` trend aralıklarını istemesi; signal eksikse trend çağrısı yapmaması, trend eksikse birleşik readiness'i kapalı tutması ve Paper readiness'in candle geçmişine bağlı olmaması.
- Strategy definition'ın sürüm, `15m/1H` tam-kat ilişkisi, EMA200 warm-up alt sınırı ve long/flat action allowlist'i; açık signal veya gelecekteki trend candle ile karar üretilememesi.
- OKX candle parser'ın `candle15m/candle1H`, `confirm=0/1`, subscription ack, future candle, bilinmeyen kanal ve sanitize error contract testleri.
- Canlı candle session'ın REST anchor öncesi bounded buffering, duplicate/out-of-order suppression, exact gap recovery, oversized gap fail-closed ve worker readiness yaşam döngüsü testleri.
- Bounded seri store'un warm-up seed, kapasite budama, immutable snapshot, duplicate/eski update ve gap/çelişkide fail-closed davranışı.
- EMA'nın `decimal` alpha/seed sonucu, yalnız son tam pencereyi kullanması, identity/continuity reddi ve `close > EMA` long izin sınırı.
- EMA20 yukarı/aşağı kesişimi, bullish trend giriş izni, trend kaybı çıkışı ve `%2` FOMO guard sınırı.
- Streaming replay'in eşit close time'da trend-first davranışı, gelecekteki trend candle'ı dışlaması, aynı girdide aynı karar/state ve historical gap'te fail-closed olması.
- Backtest kararının aynı candle'da değil sonraki candle open+latency anında fill edilmesi; spread, slippage ve iki taraflı fee'nin net getiriyi düşürmesi.
- Flat market round trip'in maliyet sonrası zarar yazması, düşük geçmiş hacimde phantom fill oluşmaması ve mevcut candle toplam hacminin open fill'e sızmaması.
- Alış fiyatının tick'e yukarı, satış fiyatının aşağı; quantity'nin lot step'e aşağı yuvarlanması, minimum altı girişin fillsiz reddi ve satılamayan remainder'ın açık/pending kalması.
- Quantized buy-and-hold'un aynı kuralları kullanması, tradable olmayan liquidation'ı reddetmesi; instrument kuralları değişince configuration hash'in değişmesi ve legacy v1 hash'inin korunması.
- Aynı input/policy'nin aynı execution raporunu üretmesi; açık pozisyon/pending target'ın zorla kapatılmadan raporlanması.
- Canonical CSV header/UTC/invariant decimal parse, raw SHA-256 kararlılığı, gap/malformed satır reddi ve erken consumer stop'ta summary oluşmaması.
- 25.000 candle fixture'ın tüm dosyayı koleksiyona almadan single-pass async tüketimi ve exact count/range kanıtı.
- Train/validation/OOS boundary sınıflandırması; parameter-selection stream'inin OOS yield etmemesi ve final planın yalnız OOS kabul etmesi.
- Aynı dataset/config/split/seed için aynı manifest; seed değişince yalnız manifest kimliğinin değişmesi ve eksik dataset summary ile manifest üretilememesi.
- Rolling walk-forward'da sabit training uzunluğu, expanding modda sabit başlangıç ve büyüyen gözlenmiş geçmiş; ardışık OOS aralıklarının bitişik/çakışmasız olması.
- Aynı schedule girdilerinin aynı indeksli pencere dizisini üretmesi; timeframe'e hizalanmayan süre ve tek tam pencereye yetmeyen dataset'in reddedilmesi.
- Aynı schedule/manifest/sonuçların aynı schedule/run/report hash üretmesi; yalnız sonuç değişince report hash'in değişmesi.
- Eksik, ters sıralı, yanlış split'li veya parameter-selection manifestli pencerenin birleşik OOS raporuna alınmaması.
- Çoklu OOS mean/median/worst/best/compound return, mean drawdown ve maliyet agregasyonlarının exact decimal sonucu.
- Gerçek SQL Server'da walk-forward run+window satırlarının atomik yazılması ve aynı rapor tekrarının ikinci kayıt oluşturmaması.
- Walk-forward orkestratörünün her pencere/timeframe için taze single-use dataset açması, pencereleri sıralı tamamlaması ve aynı girdilerde aynı run/report hash üretmesi.
- Train/validation candle'larının indicator warm-up sağlaması fakat pre-OOS pozisyon state'inin taşınmaması; ilk OOS kararının `Flat` state üzerinden değerlendirilmesi.
- Window filtresi OOS sonrasını stratejiye vermese de dataset'i EOF'ye kadar tüketerek final summary/manifest üretmesi; yetersiz warm-up ve sıfır OOS değerlendirmesinde fail-closed olması.
- Startup ve reconnect sonrasında iki timeframe'in tam warm-up ile store'a yeniden seed edilmesi; readiness açıldığında her seride en az 200 kapalı candle bulunması.
- OKX REST order-book resmi payload mapping'i, `seqId`/timestamp/bid/ask dönüşümü, symbol format guard'ı ve upstream hata mesajı sanitization contract testleri.
- OKX public instrument payload'ının Spot türü, sembol, base/quote, `tickSz`, `lotSz`, `minSz` ve `state` mapping contract testleri; suspend veya geçersiz filtrelerin fail-closed reddi.
- OKX `books5` payload parsing, `prevSeqId` continuity, subscribe acknowledgement, hata sanitization ve crossed-book reddi.
- OKX WebSocket ve REST'in tüm 1–5 bid/ask seviyelerini koruması; strict sıra, pozitif değer, top-level eşitliği ve bounded seviye sayısı ihlallerinin reddi.
- Depth-aware market buy/sell'in seviyeleri participation oranıyla tüketmesi, slippage-adjusted VWAP/fee üretmesi, toplam görünür depth'te partial kalması ve limit emrin yalnız uygun seviyelerde fill olması.
- Opt-in gerçek ağ smoke testiyle WSS subscribe sonrası BTC-USDT public snapshot ve public catalog üzerinden canlı Spot filtreleri alınması; normal test suite ağsız kalır.
- Genel incremental stream session'ın REST snapshot + sequence event'lerini sırayla yayınlaması ve gap'te fail-closed sonlanması; OKX `books5` modunun REST freshness + full WebSocket snapshot anchor ile gerçek ağda iki event üretmesi.
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
- Gözlemlenmiş 2025 v1 OOS pencereleri yeni strateji seçimine kapalıdır. Cost-derived hysteresis v2 ancak trade ve toplam maliyeti v1'e göre en az `%30` azaltır, pozitif net ve negatif olmayan benchmark excess üretir, her pencerede drawdown `%5` altında kalır ve pencerelerin en az `%60`ını kârlı kapatırsa yeni OOS'a açılır.
- Walk-forward ve farklı market regime testleri uygulanır.
- Sonuçlar strategy version, data version, seed ve config hash ile tekrar üretilebilir olmalıdır.
- Mevcut next-open execution proxy fee/spread/slippage/latency/PnL ve opsiyonel tick/lot/minimum emir kurallarını modellemektedir; order-book queue ve quantized out-of-sample kanıtı olmadan production performans iddiasında kullanılamaz.
- Trade-loss attribution aynı girdide aynı report SHA-256 değerini üretmeli; entry/exit reason, fee/spread/slippage, MFE/MAE ve holding süresini tamamlanmış trade ile eşlemelidir.
- Exit fill'inin gerçekleştiği candle'ın sonradan oluşan high/low değeri excursion'a katılmamalı; diagnostics trade limiti aşıldığında kısmi sonuç yerine hata üretmelidir.
- v3 profit-protection aktivasyon öncesinde çıkmamalı, aktivasyon sonrası peak close'tan 50 bps geri çekilmede deterministik çıkmalı ve trend-filter çıkışının önüne geçmemelidir.
- Re-entry cooldown ilk dört tamamlanmış signal candle'da giriş cross'unu engellemeli, beşinci değerlendirmede aynı koşulu kabul etmeli; replay state'i aynı girdide aynı kararları üretmelidir.
- v2-v3 validation evaluator yedi ön kayıtlı kapının tümünü birlikte istemeli; tek başarısız kapı `IsAccepted=false` ve CLI `exit 3` üretmelidir.
- v4 için canonical ADX fixture, zero-range, güçlü trend, choppy range, exact `25` sınırı, minimum 28 candle, gap/identity/overflow ve determinism testleri uygulanmıştır.
- ADX'nin yalnız entry'yi engellediği, long pozisyonda ADX düşüşünün v2 exit kararını değiştirmediği ve v1-v3 test/hash yollarının aynı kaldığı regression testleri validation ön koşuludur.
- v4 acceptance evaluator sekiz kapıyı bağımsız değerlendirir; sıfır işlem profit-factor başarısı sayılamaz ve herhangi bir başarısız kapı tüm adayı reddeder.
- v5 için yükselen/düşen/sabit DMI fixture'ları, strict `plusDI > minusDI` sınırı, entry-only davranış, v1-v4 hash regression ve sekiz v4-v5 acceptance kapısı uygulanmıştır.
- Dinamik execution için volatilite/katılım karesine bağlı monoton maliyet, ayrı bps cap'leri, `%5` fail-closed sınırı, aynı mum içine deterministik TWAP zaman dağılımı ve execution mumunun gelecekteki range/volume değerlerinin fill maliyetine sızmadığı regression testleri zorunludur.
- Dinamik execution parametreleri ayrı configuration hash üretir; legacy sabit maliyet placeholder'ları dinamik kimliği değiştirmez ve v1-v5 kilitli hash yolları korunur. Benchmark maliyet parity'si kurulmadan walk-forward acceptance fail-closed kalır.

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
- EF Core model snapshot'ında bekleyen değişiklik yok ve idempotent migration script üretilebiliyor.
- Instructions matrisi 1-100 benzersiz satır ve güncel statü özeti taşıyor.
- Gerçek ağ ve SQL Server testleri varsayılan CI'da gizlice çalıştırılmaz; kontrollü integration job eklenene kadar opt-in kanıt olarak raporlanır.

## 7. Production öncesi aşamalar

1. Offline replay.
2. Paper trading.
3. Exchange testnet.
4. Shadow mode (sinyal var, emir yok).
5. Çok düşük limitli canary live.
6. Kontrollü limit artışı.

Her aşama için en az çalışma süresi ve başarı metriği ürün kararı olarak yazılmadan sonraki aşamaya geçilmez.
