# v6 Adaptif Walk-Forward Parametre Seçim Sözleşmesi

Durum: Kabul edildi
Tarih: 2026-07-27

## Amaç

v6 ATR periyodu ve hysteresis çarpanını, yalnız karar anında tamamlanmış tarihsel
veriden seçmek ve seçilen kombinasyonu daha önce seçim metriğine girmemiş bir
sonraki out-of-sample (OOS) penceresine uygulamak.

## Pencere ve bilgi sınırı

Her walk-forward penceresinde sıra değiştirilemez:

1. Train aralığı indikatör warm-up'ı ve geçmiş bağlam için okunur.
2. Aday performansı yalnız `[TrainEndExclusive, ValidationEndExclusive)`
   validation aralığındaki gerçekleşmiş işlemlerden hesaplanır.
3. Parametre seçim akışı `ValidationEndExclusive` sınırında sonlanır; OOS mumu
   stratejiye veya execution simülatörüne verilmez.
4. En iyi uygun aday seçildikten sonra yeni dataset oturumları açılır.
5. Seçilen immutable v6 tanımı yalnız
   `[ValidationEndExclusive, OutOfSampleEndExclusive)` OOS değerlendirmesinde
   kullanılır.
6. Sonraki pencere başladığında önceki OOS artık tamamlanmış geçmiş olabilir;
   gelecekteki pencerenin verisi hiçbir zaman geriye sızamaz.

## Parametre grid'i ve skor

- Grid 1-64 benzersiz `(ATR period, multiplier)` kombinasyonu içerir ve giriş
  dizisinin immutable snapshot'ını alır.
- ATR period `2..100`, multiplier `(0, 10]` sınırındadır.
- Adayın ATR period'u ön kayıtlı signal warm-up kapasitesini aşamaz.
- Validation'da en az bir tamamlanmış işlem üretmeyen aday seçilemez.
- Birincil skor Profit Factor'dür. Kayıpsız ve pozitif brüt kârlı aday
  `decimal.MaxValue`; sıfır brüt kâr/sıfır brüt zarar adayı `0` alır.
- Eşitlik sırası: yüksek net getiri, düşük maksimum drawdown, düşük ATR period,
  düşük multiplier. Böylece grid sırası sonucu değiştirmez.
- Hiçbir aday uygun değilse OOS açılmadan işlem fail-closed biter.

## Audit kanıtı

Her seçim kaydı pencere indeksini, seçilen kombinasyonu, erişilebilir geçmiş
başlangıcını, validation başlangıç/bitiş sınırlarını, seçim skorunu, validation
getiri/drawdown/trade sayısını ve yalnız erişilen train+validation OHLCV
değerlerinden binary/stack tabanlı üretilen iki history SHA-256 kimliğini taşır.
Tam dataset hash'i OOS içeriğini kapsadığı için seçim audit kimliğine alınmaz.
OOS manifesti seçilmiş v6 parametrelerinin configuration hash'ini içerir.

## Performans ve eşzamanlılık

- Orchestrator paylaşılan mutable seçim state'i tutmaz; pencereler ve adaylar
  deterministik sırayla çalışır.
- Candidate hot loop LINQ kullanmaz.
- Grid yalnız bir kez kopyalanır; seçim döngüsünde aday koleksiyonu allocation
  üretmez.
- Her aday ve final OOS için taze, single-use streaming dataset oturumları
  kullanılır.

## Kabul sınırı

Bu mekanizma kârlılık kanıtı değildir. Dinamik execution ile buy-and-hold
benchmark maliyet parity'si tamamlanmadan yeni v6 acceptance koşusu açılamaz.
2021-2025 kilitli verileri yeniden parametre seçimine sokulamaz; forward kanıt
2026-07-27 sonrasında oluşan kesişmeyen pencerelerden gelmelidir.
