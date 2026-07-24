# Güvenlik ve Finansal Risk

**Durum:** Kabul edildi

## 1. Güvenlik ilkeleri

- Least privilege: API key üzerinde para çekme yetkisi kesinlikle bulunmaz.
- API key üzerinde futures, margin ve borrowing yetkileri bulunmaz; yalnızca gerekli Spot trade/read izinleri açılır.
- Default deny: Live trading açıkça etkinleştirilmedikçe kapalıdır.
- Defense in depth: Kod, borsa-side limit ve operasyon prosedürü birlikte çalışır.
- Auditability: Her kritik karar değiştirilemez audit izi üretir.
- Secret zero exposure: Secret log, metric label, trace, exception veya config dosyasına girmez.

## 2. Tehdit modeli

| Tehdit | Kontrol |
|---|---|
| API key sızıntısı | Vault/env secret, masking, repo secret scan, key rotation |
| Yetkisiz komut | Kimlik doğrulama, allowlist, imzalı istek, audit |
| Replay/MITM | TLS doğrulama, timestamp/nonce, dar recvWindow |
| Sahte/stale piyasa verisi | Sequence, event time, multi-source/snapshot doğrulama |
| Duplicate order | Idempotent ClientOrderId ve reconciliation |
| Dependency compromise | Paket minimizasyonu, lock/sürüm, vulnerability taraması |
| Log üzerinden veri sızıntısı | Structured allowlist logging ve redaction |
| Manuel müdahale | Account stream + periyodik reconciliation |
| Yanlış ortam | Ayrı credentials, güçlü environment banner ve fail-fast |

## 3. Secret yönetimi

- Local: .NET User Secrets veya environment variable.
- Production: yönetilen secret vault tercih edilir.
- Secret değerleri Options nesnesinde ekrana basılmaz.
- Key’ler environment ve bot instance bazında ayrılır.
- Düzenli rotation ve acil revoke prosedürü yazılır.
- IP allowlist borsa destekliyorsa zorunludur.

## 4. Risk kontrol sırası

Her yeni exposure aşağıdaki sıradan geçer:

1. Trading mode ve kill switch.
2. Exchange/account/trading status.
3. Market data freshness ve gap kontrolü.
4. Instrument filtreleri.
5. Kullanılabilir nakit ve Spot varlık bakiyesi.
6. Order notional ve position limitleri.
7. Sembol, sektör ve korelasyon exposure.
8. Günlük drawdown/loss limiti.
9. Slippage, spread ve likidite kontrolü.
10. Stop/likidasyon güvenlik mesafesi.
11. Funding/news blackout (uygulanıyorsa).

Karar `Approved`, `Resized` veya gerekçe kodlu `Rejected` olur.

## 5. Zorunlu limitler

- İşlem başına maksimum risk yüzdesi.
- Günlük maksimum realized + configurable unrealized kayıp.
- Sembol başına maksimum notional.
- Toplam gross/net exposure.
- Maksimum açık emir ve pozisyon sayısı.
- Sell işlemlerinde maksimum kullanılabilir varlık miktarı; negatif pozisyon kesinlikle yasak.
- Maksimum spread/slippage.
- Maksimum veri yaşı ve clock offset.
- Maksimum DCA/grid kademe sayısı; varsayılan kapalı.
- Ardışık hata/rejection sonrası circuit breaker.

Değerler ürün kararıdır; güvenli varsayılan live işlemi engellemektir.

## 6. Kaldıraçsız Spot sınırı

- Leverage daima `1x` ekonomik exposure ile sınırlıdır; borsa leverage özelliği kullanılmaz.
- Futures, perpetual, options, margin, borrowing ve short emir yolları uygulanmaz.
- Bot yalnızca sahip olduğu varlığı satabilir.
- Spot dışı endpoint veya instrument yanlışlıkla yapılandırılırsa uygulama fail-fast davranır.
- Aylık getiri hedefi pozisyon boyutunu otomatik büyütemez ve risk profilini değiştiremez.

## 7. Kill switch seviyeleri

- **Pause:** Yeni intent üretme; açık koruyucu emirleri koru.
- **Cancel:** Yeni intent’i durdur ve açık giriş emirlerini iptal et.
- **Flatten:** Yetkili operatör onayıyla pozisyonları kontrollü kapat.
- **Emergency:** Sistemik riskte önceden tanımlı acil politika.

“Flatten all” geri dönüşü zor finansal işlem olduğundan kimlik doğrulama, açık onay ve audit gerektirir.

## 8. Live readiness kontrol listesi

- Paper ve testnet kabul testleri tamamlandı.
- API key withdrawal yetkisiz ve IP allowlist’li.
- API key futures/margin/borrowing yetkilerinden arındırılmış.
- Risk limitleri iki kişi/iki aşamalı gözden geçirildi.
- Server-side stop doğrulandı.
- Restart/reconciliation ve unknown order senaryoları test edildi.
- Kill switch tatbikatı yapıldı.
- Alarm kanalları ve yedek kanal test edildi.
- Backup/restore ve rollback test edildi.
- Düşük notional canary dönemi tanımlandı.
- Operatör live moda açıkça onay verdi.
