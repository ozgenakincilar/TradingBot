# ADR-0028: Dinamik Buy-and-Hold Benchmark TWAP Paritesi

Durum: Kabul edildi

Tarih: 2026-07-27

## Bağlam

Dinamik strateji execution modeli volatilite, hacim katılımı ve child-order
maliyetini hesaplarken benchmark sabit spread/slippage kullanıyordu. Bu fark
benchmark excess metriğini bilimsel olarak karşılaştırılamaz hale getiriyor ve
v6 acceptance akışını fail-closed tutuyordu.

## Karar

Strateji simülatörü ve benchmark için tek allocation-free hot-path
`DynamicTwapExecutionModel` çekirdeği kullanılacaktır. Benchmark alışı önceki
tamamlanmış mum referansıyla OOS açılışından itibaren, terminal satışı yalnız OOS
sonunda bilinen son kapalı mumla çalışacaktır. Her iki yön aynı dinamik cost
quote, `%5` toplam kapasite, bounded child, quantization, fee ve paper fill
motorunu kullanacaktır. Terminal kapasite tam pozisyonu kapatamıyorsa sonuç
üretilmeyecektir.

v6 acceptance CLI yalnız kilitli dinamik policy, tam instrument kuralları,
önceden kayıtlı dokuz ATR kombinasyonu ve forward tarih/pencere sınırlarıyla
çalışacaktır. Exit code `3` yalnız üretilmiş raporun acceptance reddine ayrılır.

## Sonuçlar

- Benchmark excess like-for-like execution maliyetiyle hesaplanır.
- Dinamik benchmark kilidi kaldırılır; veri/tarih/acceptance kapıları korunur.
- Ortak generic struct consumer yolu child döngüsünde delegate, LINQ veya child
  koleksiyonu oluşturmaz.
- Eski sabit benchmark ve v1-v5 configuration hash yolları değişmez.
- Forward veri oluşmadan kârlılık veya production uygunluğu iddia edilemez.

## Alternatifler

- Benchmark'ta dinamik formülleri kopyalamak: zamanla ayrışma riski nedeniyle
  reddedildi.
- Terminal pozisyonu `%5` kapasiteyi aşarak kapatmak: likidite varsayımını
  bozduğu için reddedildi.
- OOS mumunun range/hacmini aynı mum açılışındaki alışa vermek: look-ahead
  ürettiği için reddedildi.
