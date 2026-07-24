# ADR-0004: Microsoft SQL Server Persistence

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

Geliştirme ortamında Microsoft SQL Server kuruludur. Sistem; emir, execution, pozisyon, risk kararı, audit ve zaman serisi verilerini transaction ve restart/reconciliation gereksinimleriyle kalıcılaştırmalıdır.

## Karar

- Ana ilişkisel veritabanı Microsoft SQL Server olacaktır.
- Veri erişimi Infrastructure katmanında EF Core SQL Server provider ile uygulanacaktır.
- Modüller aynı database içinde ayrı SQL schema'larıyla (`market_data`, `strategy`, `risk`, `execution`, `portfolio`, `operations`) ayrılacaktır.
- Para ve miktar alanları açık precision/scale değerli `decimal` kolonlar kullanacak; SQL Server `money` tipleri kullanılmayacaktır.
- Optimistic concurrency için uygun aggregate tablolarda `rowversion` kullanılacaktır.
- Emir ve audit verisi güvenilir transaction'larla; yüksek hacimli market data ise batch/bulk write ve retention politikasıyla yönetilecektir.
- Migration'lar sürümlenecek, production deployment öncesi script olarak incelenecek ve backup/restore testi yapılacaktır.

## Sonuçlar

Olumlu:

- Mevcut yerel altyapı kullanılabilir.
- Güçlü transaction, constraint, indexing, backup ve operasyon araçları sağlanır.
- EF Core ile provider ayrıntıları Infrastructure sınırında tutulur.

Bedeller:

- SQL Server sürümü ve lisansına bağlı özellik sınırları deployment öncesi doğrulanmalıdır.
- Çok yüksek hacimli tick verisi için batch yazım, partitioning ve arşivleme ayrıca tasarlanmalıdır.
- Provider'a özgü migration ve sorgular portability maliyeti oluşturabilir.

## Alternatifler

- PostgreSQL: Teknik olarak uygun olmakla birlikte kurulu altyapı SQL Server olduğu için seçilmedi.
- SQLite: Concurrency ve production operasyon gereksinimleri için seçilmedi.
- Ayrı time-series database: İlk aşamada gereksiz operasyonel maliyet nedeniyle ertelendi; ölçülmüş ihtiyaç oluşursa yeniden değerlendirilir.
