# ADR-0005: ACID, CAP ve Tutarlılık Modeli

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

TradingBot hem SQL Server içindeki finansal durumu hem de transaction'a katılamayan dış borsa durumunu yönetir. Bir emir, borsa HTTP/WebSocket çağrısı ve yerel veritabanı tek bir distributed ACID transaction içinde güvenli biçimde tutulamaz. Ağ bölünmesi veya timeout sırasında kullanılabilir kalmaya çalışmak duplicate order ya da kontrolsüz exposure oluşturabilir.

## Karar

### Yerel ACID sınırı

SQL Server içindeki tek bir iş kararına ait değişiklikler kısa ACID transaction içinde atomik kaydedilir. Aşağıdaki örnekler aynı transaction sınırındadır:

- Order state değişikliği, audit kaydı ve outbox mesajı.
- Fill kaydı, order gerçekleşen miktarı ve ilgili portfolio projection güncellemesi.
- Risk rezervasyonu ve yerel order oluşturma.
- Reconciliation sonucu, fark kaydı ve audit olayı.

Transaction başarısız olursa tüm yerel değişiklikler rollback edilir. Aggregate güncellemelerinde `rowversion` ile optimistic concurrency uygulanır; retry yalnızca tüm transaction'ın güvenle tekrar çalıştırılabildiği durumda yapılır.

### Dış çağrı sınırı

Borsa REST/WebSocket çağrısı boyunca SQL transaction açık tutulmaz:

1. Kısa transaction ile order `Submitting` durumuna ve outbox/audit kaydına alınır.
2. Transaction commit edilir.
3. İdempotent `ClientOrderId` ile borsaya çağrı yapılır.
4. Sonuç ikinci kısa transaction ile `Open`, `Rejected` veya `Unknown` olarak kaydedilir.
5. Sonuç belirsizse aynı ekonomik emir körlemesine tekrarlanmaz; borsa sorgulanarak reconcile edilir.

Bu süreç Saga/process manager, idempotency, transactional outbox ve reconciliation ile eventual consistency sağlar.

### CAP tercihi

CAP, yalnızca ağ bölünmesi olan dağıtık sınırlar için değerlendirilir. Borsa ile bağlantı bölündüğünde Execution, Risk ve Portfolio tarafında **tutarlılık kullanılabilirliğe tercih edilir (CP eğilimi)**:

- Kesin emir/pozisyon durumu olmadan yeni exposure oluşturulmaz.
- Belirsiz sonuç `Unknown` olarak kaydedilir ve ilgili işlem hattı durdurulur.
- Market data stale veya sequence gap içeriyorsa ilgili sembolde yeni sinyal/emir durdurulur.
- Reconciliation tamamlanmadan otomatik devam edilmez.

Telemetry, dashboard ve analitik projection'larda sınırlı eventual consistency kabul edilir; bunlar emir kararının yetkili kaynağı olamaz.

## Sonuçlar

Olumlu:

- Duplicate order ve tutarsız finansal state riski azalır.
- SQL lock süresi dış ağ gecikmesinden etkilenmez.
- Restart ve timeout sonrası durum borsa gerçeğinden yeniden kurulabilir.
- Gelecekte modüller ayrıldığında tutarlılık politikası korunur.

Bedeller:

- Sistem bazı ağ arızalarında işlem yapmayı durdurur.
- Outbox dispatcher, idempotency store ve reconciliation worker gerekir.
- Kullanıcı arayüzü/projection kısa süre geriden gelebilir.
- Saga ve `Unknown` durumları için kapsamlı test gerekir.

## Alternatifler

- Borsa çağrısı boyunca SQL transaction tutmak: Uzun lock ve yine de atomiklik sağlayamaması nedeniyle reddedildi.
- Timeout sonrası doğrudan order retry: Duplicate order riski nedeniyle reddedildi.
- Kullanılabilirlik öncelikli AP execution: Belirsiz state ile sermaye riski yarattığı için reddedildi.
- Distributed transaction/2PC: Borsa tarafından desteklenmediği ve operasyonel olarak uygun olmadığı için reddedildi.
