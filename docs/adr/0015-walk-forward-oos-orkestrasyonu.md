# ADR-0015: Walk-forward OOS orkestrasyonu ve state izolasyonu

**Durum:** Kabul edildi
**Tarih:** 2026-07-25

## Bağlam

Walk-forward schedule, sonuç kimliği ve SQL persistence tek başına tarihsel veriyi güvenli biçimde çalıştırmaz. Her pencerenin indicator warm-up için geçmişi görmesi, buna rağmen train/validation dönemindeki sinyallerin pozisyon state'ini OOS'a taşımaması gerekir. Tek kullanımlık streaming datasetlerin erken bırakılması da tamamlanmış veri özeti ve manifest kanıtını geçersiz kılar.

## Karar

- Her pencere sıralı çalışır ve signal/trend timeframe'leri için factory üzerinden taze, tek kullanımlık dataset açılır.
- Pencere akışı `[split.StartInclusive, split.OutOfSampleEndExclusive)` candle'larını stratejiye verir; kaynak akış manifest özeti için EOF'ye kadar tüketilir.
- Train ve validation candle'ları yalnız bounded indicator warm-up amacıyla kullanılır.
- Ekonomik değerlendirme `ValidationEndExclusive` sınırında başlar; bu sınırda sanal strategy position kesin olarak `Flat` durumundadır.
- OOS öncesi entry/exit kararları ve pozisyon state'i OOS'a taşınmaz. İlk fill en erken ilk OOS kararından sonraki signal candle open proxy'sinde oluşabilir.
- Train+validation geçmişi iki timeframe'in minimum warm-up süresini karşılamıyorsa veya OOS'ta hiç değerlendirme oluşmuyorsa çalışma fail-closed biter.
- İki dataset EOF'ye ulaşmadan final-OOS manifest veya birleşik rapor üretilemez. Bir pencerenin hatası kısmi rapor döndürmez.
- Aynı dataset, schedule, strategy, execution policy ve seed aynı manifest/run/report hash'lerini üretir.

## Sonuçlar

- Look-ahead ve pre-OOS position leakage için açık bir uygulama sınırı oluşur.
- Bellek tüketimi candle sayısıyla büyümez; aynı anda yalnız aktif pencerenin bounded strateji pencereleri ve execution state'i tutulur.
- Dataset dosyaları pencere başına yeniden taranır. Bu, bellek güvenliği ve bağımsız kanıt karşılığında kabul edilen I/O maliyetidir.
- Orkestrasyon altyapısı gerçek tarihsel dosyaları çalıştırmaya hazırdır; çoklu market-regime datasetleri ve kabul raporu ayrıca sağlanmalıdır.

## Alternatifler

- Train döneminde oluşturulan sanal pozisyonu OOS'a taşımak, bağımsız OOS ölçümünü kirlettiği için reddedildi.
- Tüm dataset'i belleğe almak, büyük tarihsel serilerde bounded-memory gerekliliğini ihlal ettiği için reddedildi.
- Window sınırında okumayı durdurmak, EOF özeti ve raw dataset kanıtını oluşturamadığı için reddedildi.
- Pencereleri aynı single-use stream üzerinde paralel çalıştırmak, deterministik sahiplik ve kaynak sınırlarını karmaşıklaştırdığı için reddedildi.
