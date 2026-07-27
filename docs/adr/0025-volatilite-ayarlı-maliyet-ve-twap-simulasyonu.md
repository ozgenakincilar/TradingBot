# ADR-0025: Volatilite ayarlı execution maliyeti ve TWAP simülasyonu

**Durum:** Kabul edildi

**Tarih:** 2026-07-27

## Bağlam

v1-v5 historical raporları sabit sentetik spread/slippage ve önceki kapalı mum hacmine uygulanan katılım sınırıyla üretilmiştir. Bu sonuçların configuration hash'leri kilitli kanıttır. Sabit maliyet, volatilite ve emir/hacim oranı büyüdüğünde execution etkisini olduğundan düşük gösterebilir; tek partial fill ise mum içindeki zaman dağılımını temsil etmez.

## Karar

- Yeni execution modeli opt-in `VolatilityAdjustedExecutionPolicy` sözleşmesidir; v1-v5 legacy yolunu ve hash'lerini değiştirmez.
- Spread ve slippage; son tamamen kapanmış mumun `(High-Low)/Close` değerinin karesi ile kümülatif child miktarının aynı mum hacmine katılım oranının karesinden hesaplanır. Sonuçlar ayrı alt/üst bps sınırlarıyla fail-closed tutulur.
- Dinamik modda toplam likidite katılımı `%5`i aşamaz. Daha yüksek policy startup/manifest öncesinde reddedilir.
- Uygulanabilir miktar 2-64 arası bounded child sayısına bölünür. Child zamanları bir sonraki signal mumunun `open + latency` anından mum kapanışından önceki noktalara deterministik olarak yayılır.
- Child fiyatları execution mumunun yalnız açılış fiyatını ve karar anında bilinen önceki kapalı mumun range/volume bilgisini kullanır. Execution mumunun daha sonra oluşacak high/low/volume değerleri fill fiyatına sızmaz.
- Aynı mumdaki child toplamı `%5` kapasitesini aşmaz; kalan hedef sonraki muma pending taşınır. Tick/lot/minimum-notional kuralları her child için uygulanır.
- Cost input, quote, policy ve tamamlanmış candle referansı `readonly record struct` olarak taşınır. Hot-path hesap koleksiyon veya LINQ oluşturmaz; simulator ortak mutable state tutmaz.
- Dinamik configuration kimliği `volatility-adjusted-twap-backtest-v1` şemasıyla tüm cost/TWAP parametrelerini kapsar; artık kullanılmayan legacy sabit spread/slippage placeholder değerleri bu kimliğe katılmaz.
- Buy-and-hold benchmark aynı dinamik maliyet ve katılım sözleşmesine geçirilmeden dinamik model walk-forward acceptance'a giremez; mevcut kod bu durumu fail-closed reddeder.

## Sonuçlar

- Volatilite ve katılım yükseldikçe maliyet doğrusal olmayan biçimde artar ve üst sınırda bounded kalır.
- Büyük emirler tek fill yerine mum içine yayılan deterministik child fill'ler üretir; toplam katılım korunur.
- OHLCV hâlâ gerçek order-book ve intrabar trade akışının proxy'sidir. Queue position, hidden liquidity ve gerçek child price path kanıtı sonraki aşamada gerekir.
- Bu değişiklik tek başına yeni strateji, pozitif expectancy veya paper/testnet/live izni değildir.

## Alternatifler

- Mevcut sabit 20/10 bps değerlerini tüm geçmişe uygulamak volatilite ve ölçek etkisini sakladığı için reddedildi.
- Execution mumunun tamamlanmış high/low/volume değerini aynı mum içindeki fill'e vermek look-ahead ürettiği için reddedildi.
- `%5` kapasitesini her child için yeniden kullanmak toplam katılımı child sayısıyla çarpacağı için reddedildi.
- Sınırsız child listesi üretmek allocation ve çalışma süresi sınırlarını bozacağı için reddedildi.
