# ADR-0020: Bounded cumulative order-book depth ve paper market impact

**Durum:** Kabul edildi

**Tarih:** 2026-07-26

## Bağlam

OKX `books5` WebSocket ve REST order-book yanıtları beş fiyat seviyesi taşımasına rağmen mevcut adaptör yalnız en iyi bid/ask seviyesini `PaperTopOfBookSnapshot` içine aktarıyordu. Paper execution bu nedenle büyük bir market emrinin yalnız top-of-book miktarıyla sınırlı olduğunu görüyor, sonraki görünür seviyelerde oluşacak ek fiyat etkisini hesaplayamıyordu.

Bu eksiklik `instructions.md` içindeki ters yönlü slippage/depth kuralını kısmen açık bırakır. Bununla birlikte beş seviyeli snapshot, gerçek limit-order queue position veya tam piyasa derinliği değildir.

## Karar

- `PaperTopOfBookSnapshot`, mevcut top-of-book alanlarını korur ve opsiyonel immutable bid/ask depth dizileri taşır.
- Depth iki taraflı, 1–5 seviye, pozitif fiyat/miktar ve strict fiyat sıralı olmak zorundadır. İlk seviyeler mevcut best bid/ask fiyat ve miktarıyla birebir eşleşir.
- Bid seviyeleri yüksekten düşüğe, ask seviyeleri düşükten yükseğe sıralanır; eksik taraf, crossed book, duplicate/ters fiyat veya beşten fazla seviye fail-closed reddedilir.
- OKX `books5` WebSocket adaptörü payload'daki tüm seviyeleri korur. REST recovery isteği `sz=5` kullanır ve aynı domain sözleşmesini üretir.
- Depth bulunan snapshot'ta paper market execution seviyeleri en iyi fiyattan başlayarak tüketir. Her seviyedeki görünür miktar `MaximumLiquidityParticipation` ile sınırlandırılır.
- Yönsel slippage her seviyeye kullanıcı aleyhine uygulanır; tek `PaperFill` fiyatı gerçekleşen seviyelerin volume-weighted average price değeridir. Komisyon toplam gerçekleşen notional üzerinden hesaplanır.
- Limit emir yalnız slippage-adjusted limit koşulunu sağlayan sıralı seviyeleri tüketir; sonraki uygun olmayan seviyede durur ve varsa kısmi fill'i döndürür.
- Depth taşımayan legacy/sentetik snapshot mevcut top-of-book matematiğini aynen kullanır; backtest kilitli raporları değişmez.

## Sonuçlar

- Paper market emirleri görünür beş seviyede deterministik cumulative depth ve market impact hesabı yapar.
- Büyük emir, tek seviyeli modele kıyasla daha kötü VWAP ve bounded partial fill üretebilir.
- REST recovery ve WebSocket olayları aynı depth invariant'larına tabidir.
- Snapshot boyutu taraf başına beş seviye ile bounded kalır; sınırsız collection veya tick arşivi oluşturulmaz.
- Model yalnız görünen aggregated miktarı kullanır; emir sırası, gizli likidite, market-maker davranışı ve cancel latency henüz modellenmez.

## Alternatifler

- Top-of-book miktarını tüm piyasa likiditesi saymak, büyük emir fiyat etkisini gizlediği için reddedildi.
- Beş seviyeyi sınırsız liste olarak taşımak, bounded-memory ve payload sözleşmesini zayıflattığı için reddedildi.
- Seviyelerden ayrı ayrı fill üretmek, mevcut tek-event/tek-fill settlement sözleşmesini büyüteceği için bu dilimde reddedildi; matematiksel olarak eşdeğer VWAP fill seçildi.
- Bu modeli “queue replay” olarak adlandırmak, sıra verisi bulunmadığından yanıltıcı olacağı için reddedildi.
