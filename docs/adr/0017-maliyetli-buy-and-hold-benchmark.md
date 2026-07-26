# ADR-0017: Maliyetli buy-and-hold walk-forward benchmark

**Durum:** Kabul edildi  
**Tarih:** 2026-07-26

## Bağlam

Stratejinin pozitif getiri üretmesi tek başına değer kattığını göstermez. Aynı OOS dönemde, aynı başlangıç sermayesi ve spot allocation ile pasif BTC tutmanın sonucu bilinmeden strateji performansı doğru yorumlanamaz. Benchmark'ın komisyon, spread ve slippage maliyetlerini yok sayması da karşılaştırmayı sistematik biçimde yanıltır.

## Karar

- Her walk-forward OOS penceresi için bağımsız buy-and-hold benchmark hesaplanır.
- Benchmark ilk OOS candle open fiyatında alır ve son OOS candle close fiyatında raporlama amacıyla likide eder.
- Başlangıç sermayesi, quote allocation, sentetik spread, komisyon ve slippage strateji execution policy ile aynıdır.
- Kaldıraç, short, yeniden dengeleme ve ara dönem işlem yoktur.
- Benchmark candle serisi OOS başlangıç ve bitiş sınırlarını eksiksiz kapsamalı, timeframe ile eşleşmeli ve contiguous olmalıdır; aksi durumda rapor üretilmez.
- Net/gross getiri, iki yönlü maliyetler, maksimum drawdown ve stratejinin benchmark üzerindeki excess getirisi raporlanır.
- Benchmark sonucu report SHA-256 kimliğine ve normalize SQL window kaydına dahildir. Rapor şeması `walk-forward-report-v2` olur.
- Eski v1 SQL kayıtları korunur; yeni benchmark sütunları onlar için `NULL`, v2 kayıtları için tam set olarak yazılır.

## Sonuçlar

- Strateji getirisi, aynı sermaye ve maliyet varsayımlarıyla pasif alternatife karşı ölçülebilir.
- Benchmark'ı geçen pencere sayısı, ortalama excess net getiri ve bileşik benchmark getirisi deterministik rapora girer.
- Benchmark yalnızca karşılaştırma ölçütüdür; strateji, risk engine veya aylık `%10` hedefi için emir sinyali değildir.
- Aylık segmentasyon ayrı bir equity-series raporlama dilimi olarak kalır.

## Alternatifler

- Maliyetsiz buy-and-hold, adil karşılaştırma sağlamadığı için reddedildi.
- Benchmark'ı yalnızca doküman/harici spreadsheet olarak tutmak, reproducibility ve report identity zincirini bozduğu için reddedildi.
- Her OOS penceresinde sermayeyi bir önceki pencereden taşımak, bağımsız pencere karşılaştırmasını değiştirdiği için reddedildi.
