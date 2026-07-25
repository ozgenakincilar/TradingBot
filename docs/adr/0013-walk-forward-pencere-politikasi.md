# ADR-0013: Walk-forward pencere politikası

**Durum:** Kabul edildi
**Tarih:** 2026-07-25

## Bağlam

Tek bir train/validation/out-of-sample ayrımı, stratejinin farklı piyasa dönemlerinde kararlı olduğunu kanıtlamaz. Rastgele veya elle seçilen pencereler selection bias üretebilir; çakışan out-of-sample aralıkları da aynı sonucu bağımsız kanıt gibi sayabilir.

## Karar

- Walk-forward schedule yalnız UTC dataset başlangıç/bitişi ve sabit `TimeSpan` sürelerinden deterministik üretilir.
- Training modu explicit olarak `Rolling` veya `Expanding` seçilir.
- Rolling mod sabit training süresini ileri taşır; expanding mod ilk başlangıcı koruyup yalnız o anda gözlenmiş geçmişi training'e ekler.
- Her pencere train, validation ve out-of-sample için kesişmeyen `[start,end)` split üretir.
- Schedule ilerleme adımı out-of-sample süresine eşittir. Ardışık OOS aralıkları bitişik olur; çakışma veya boşluk oluşmaz.
- Training, validation ve OOS süreleri hem signal hem trend timeframe'in tam katı; dataset sınırları da her iki timeframe'e hizalı olmak zorundadır.
- Tam train+validation+OOS içermeyen son dataset kuyruğu pencere oluşturmaz. Hiç tam pencere yoksa işlem fail-closed sonlanır.
- Tek schedule en fazla 10.000 pencere üretebilir; kontrolsüz koleksiyon büyümesine izin verilmez.
- Önceki pencerenin OOS verisi yalnız sonraki kronolojik pencereye gelindiğinde gözlenmiş geçmiş sayılabilir. Aynı pencerenin parameter-selection akışı kendi OOS bölümünü göremez.

## Sonuçlar

- Aynı girişler aynı sırada ve aynı indekslerle aynı pencereleri üretir.
- Rolling ve expanding deneyler örtük varsayım olmadan ayrı ayrı raporlanabilir.
- Çoklu OOS sonuç toplama, schedule kimliği ve SQL result persistence sonraki dilimde tamamlanacaktır.
- Walk-forward sonucu tek başına canlı kârlılık veya aylık getiri garantisi değildir.

## Alternatifler

- Rastgele zaman serisi split'i look-ahead ve rejim karışması nedeniyle reddedildi.
- OOS süresinden küçük ilerleme adımı, OOS aralıklarını çakıştırdığı için reddedildi.
- Sınırsız pencere üretimi bellek ve çalışma süresi riski nedeniyle reddedildi.
