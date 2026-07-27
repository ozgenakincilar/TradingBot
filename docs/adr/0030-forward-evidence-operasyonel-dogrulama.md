# ADR-0030: Forward Evidence Operasyonel Doğrulama Sınırı

Durum: Kabul edildi

Tarih: 2026-07-27

## Bağlam

ADR-0029 veri toplama ve değerlendirme hattını kurdu. Kod seviyesi testler;
gerçek SQL trigger davranışı, yedi bölümlük tam prova, public endpoint uyumu ve
uzun çalışan worker'ın temel sağlık durumunu tek başına kanıtlamıyordu.

## Karar

Gerçek SQL testi yalnız `TradingBotForwardEvidenceTest` catalog'unda opt-in
çalışacaktır. Yedi pencereli prova production sınıflarını ve streaming CSV
dosyalarını kullanacaktır. Public OKX smoke komutu evidence persistence'tan ayrı
kalacaktır. Worker telemetry'si OpenTelemetry gelene kadar atomik process-local
sayaçlarla tutulacak ve HTTP health/metric uçlarına açılacaktır. Evidence root
tek bir writer tarafından file lease ile sahiplenilecektir.

## Sonuçlar

- Trigger ve transaction semantiği gerçek SQL Server üzerinde kanıtlanabilir.
- Büyük prova bütün dataset'i belleğe almaz; CI süresine yaklaşık 45–60 saniye ekler.
- Smoke testi fiyat veya payload yayınlamaz ve yalnız iki bounded istek yapar.
- Telemetry restart ile sıfırlanır; kalıcı zaman serisi ve dashboard değildir.
- File lease aynı storage root için ikinci process'i engeller; çok düğümlü
  deployment hâlâ tek replica ve paylaşılan volume operasyon kuralına tabidir.
- v6 parametreleri ve acceptance kapıları değişmez.

## Alternatifler

- Sentetik testte evaluator'ı mock'lamak: hash determinizmini kanıtlamadığı için
  reddedildi.
- Integration test kayıtlarını commit edip sonra silmek: append-only sözleşmeye
  aykırı olduğu için transaction rollback seçildi.
- Diagnostic veriyi ana evidence dizinine yazmak: bilimsel veri zincirini
  kirleteceği için reddedildi.
