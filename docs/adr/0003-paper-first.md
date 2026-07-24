# ADR-0003: Paper-First ve Live Deny-by-Default

**Durum:** Kabul edildi  
**Tarih:** 2026-07-24

## Bağlam

Execution, reconciliation veya risk kontrolündeki tek hata gerçek sermaye kaybına yol açabilir. Henüz borsa, ürün ve risk limitleri kesinleşmemiştir.

## Karar

- Varsayılan ve başlangıç modu Paper’dır.
- Live mod kod/config ile örtük biçimde aktif olamaz.
- Paper, testnet, shadow ve düşük limitli canary aşamaları sırayla tamamlanır.
- Live readiness kontrol listesi ve açık operatör onayı olmadan gerçek emir gönderilmez.

## Sonuçlar

- Geliştirme hatalarının finansal etkisi sınırlanır.
- Live’a geçiş daha uzun sürer fakat kanıta dayalı olur.
- Paper fill modelinin gerçekçi olması ayrı bir kalite gereksinimidir.

## Alternatifler

Doğrudan düşük tutarlı live test, protokol hatalarının sermayeye etkisi nedeniyle reddedildi.
