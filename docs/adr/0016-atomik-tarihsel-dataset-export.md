# ADR-0016: Atomik ve sayfalı tarihsel dataset export

**Durum:** Kabul edildi
**Tarih:** 2026-07-25

## Bağlam

Walk-forward altyapısının gerçek piyasa verisiyle çalışması için OKX kapalı candle geçmişinin canonical CSV formatına güvenilir biçimde aktarılması gerekir. OKX `history-candles` endpoint'i ters kronolojik yanıt verir, istek başına en fazla 100 kayıt ve IP başına 20 istek/2 saniye sınırı uygular. Eksik sayfa, gap, cancellation veya disk hatasında yarım dosyanın geçerli araştırma datası gibi görünmesi kabul edilemez.

## Karar

- Export aralığı UTC, timeframe boundary'lerine hizalı ve `[fromInclusive,toExclusive)` olarak tanımlanır.
- Uygulama aralığı en fazla 100 candle'lık ardışık sayfalara böler; sayfalar sıralı istenir ve başlangıçlar arasında en az 100 ms pacing uygulanır.
- Her sayfa exact count, instrument, timeframe ve contiguity bakımından yeniden doğrulanır. Eksik veya fazla sayfa fail-closed olur.
- Çıktı BOM'suz UTF-8, LF newline, round-trip UTC timestamp ve invariant `decimal G29` formatıyla canonical CSV üretilir.
- Writer aynı dizinde benzersiz `.partial-*` dosyasına async ve 64 KiB buffer ile yazar; tam flush ve streaming SHA-256 sonrasında overwrite olmadan atomik rename yapar.
- Cancellation veya herhangi bir hata final dosya yayımlamaz ve yalnız o çalışmaya ait partial dosyayı temizler. Var olan hedef dosya hiçbir zaman overwrite edilmez.
- Export artifact'i file path, export zamanı, source/schema/raw SHA-256 descriptor ve exact count/range summary taşır.
- Büyük tarihsel dataset dosyaları Git'e alınmaz; `data/` repository dışında bırakılır.

## Sonuçlar

- Aynı candle içeriği aynı canonical raw SHA-256 kimliğini üretir.
- Ağ ve disk tarafında tüm seri belleğe alınmaz; yalnız tek bounded API sayfası ve writer buffer'ı tutulur.
- Endpoint limitleri değişirse adapter ve ADR birlikte gözden geçirilmelidir.
- Global OKX request ağırlık/response-header limiter hâlâ ayrı operasyonal çalışmadır; bu karar exporter'ın belgelenmiş endpoint hızını aşmamasını sağlar.

## Alternatifler

- Tek büyük REST isteği, resmî 100 kayıt sınırını ihlal ettiği için reddedildi.
- Tüm candle'ları bellekte biriktirip sonra yazmak, büyük dosya güvenliği kuralını ihlal ettiği için reddedildi.
- Hedef dosyaya doğrudan yazmak, kesintide yarım dataset'i geçerli gösterebildiği için reddedildi.
- Var olan dosyayı overwrite etmek, araştırma kanıtının sessizce değişmesine yol açtığı için reddedildi.
