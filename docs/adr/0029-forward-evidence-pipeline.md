# ADR-0029: Otonom ve Değiştirilemez Forward Evidence Pipeline

Durum: Kabul edildi

Tarih: 2026-07-27

## Bağlam

v6 parametre seçimi ve benchmark parity tamamlandı; ancak taze forward verinin
manuel indirilmesi veri seçme, eksik mum, dosya değiştirme ve sonuçtan sonra eşik
oynatma riski taşıyordu. Acceptance için gereken veri henüz oluşmadı.

## Karar

OKX TR public REST verisi Polly tabanlı standart HTTP resilience zinciriyle
toplanacaktır. Her 30 günlük UTC bölüm, bounded paging ve exact continuity
kontrolünden sonra iki canonical CSV ve bir SHA-256 manifest olarak atomik
mühürlenecektir. Artifact ve evaluation metadata'sı SQL'de uygulama portu ve
UPDATE/DELETE reddeden trigger'larla append-only tutulacaktır.

İlk otomatik v6 koşusu 30 günlük expanding train, 30 günlük validation ve beş
30 günlük OOS için yedi mühürlü bölüm olduğunda yapılacaktır. Sonraki her bölüm
yeni bir koşu üretir. PR #39 v6 configuration factory'si tek kaynak olarak
kullanılacak; runtime ayarları strateji/grid/acceptance değerlerini değiştiremez.

## Sonuçlar

- Restart, tekrar çağrı ve aynı pencere yarışı idempotenttir.
- Eksik/açık/çelişkili mum kanıt dosyasına dönüşmez.
- Büyük dataset belleğe alınmaz; REST, CSV, SHA-256 ve replay streaming çalışır.
- SQL kaydı ve dosya publish tek transaction değildir; bu nedenle dosya önce
  atomik ve idempotent publish edilir, SQL ikinci adımda aynı hash ile tamamlanır.
- En erken acceptance tarihi veri oluşumuna bağlıdır; kod kârlılık iddiası üretmez.

## Alternatifler

- Her candle'ı doğrudan mutable CSV'ye append etmek: crash/torn-write ve geçmişi
  değiştirme riski nedeniyle reddedildi.
- Yalnız WebSocket verisini saklamak: bağlantı boşluklarında kesin gap-filling
  kanıtı vermediği için reddedildi.
- Acceptance değerlerini appsettings'e açmak: sonuç sonrası tuning riski nedeniyle
  reddedildi.
