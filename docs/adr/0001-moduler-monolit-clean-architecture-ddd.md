# ADR-0001: Modüler Monolit, Clean Architecture ve DDD

**Durum:** Kabul edildi  
**Tarih:** 2026-07-24

## Bağlam

Trading sistemi güçlü domain kuralları, dış borsa bağımlılıkları ve yüksek operasyonel güvenlik gerektirir. İlk sürümde mikroservislerin ağ, deployment ve distributed consistency maliyeti için kanıtlanmış ihtiyaç yoktur.

## Karar

- Deployment modeli modüler monolittir.
- Bağımlılık yönü Clean Architecture ile içeri doğrudur.
- Karmaşık finansal davranış taktiksel DDD ile modellenir.
- Dış sistemler Ports & Adapters ile izole edilir.
- Modül veri sahipliği korunur; doğrudan çapraz tablo bağımlılığı oluşturulmaz.

## Sonuçlar

Olumlu:

- Domain borsa SDK’sı ve persistence’tan bağımsız test edilir.
- İlk sürümün operasyonu basit kalır.
- Modül sınırları gelecekte kontrollü servis ayrışmasına izin verir.

Bedeller:

- Namespace/proje/veri sınırları disiplinle korunmalıdır.
- Tek process bazı hata ve ölçekleme sınırlarını paylaşır.

## Alternatifler

- Onion Architecture: Benzer bağımlılık modelinden dolayı ayrıca seçilmedi.
- SOA/mikroservis: Erken operasyonel ve tutarlılık maliyeti nedeniyle ertelendi.
- Katmansız monolit: Domain ve dış bağımlılıkları karıştıracağı için reddedildi.
