# ADR-0019: Backtest instrument quantization ve minimum emir kuralları

**Durum:** Kabul edildi

**Tarih:** 2026-07-26

## Bağlam

Mevcut next-open simulator fee, spread, slippage, latency ve likidite katılımını modellemekte; ancak sentetik fiyat ve miktarlar borsanın price tick, quantity step, minimum quantity ve minimum notional sınırlarına yuvarlanmamaktaydı. Bu durum gerçek borsada gönderilemeyecek miktarların veya fiyatların backtest fill'i üretmesine ve strateji/benchmark karşılaştırmasının iyimser olmasına yol açabilir.

2025 v1 OOS ve 2024 v2 validation raporları kilitli kanıttır. Yeni execution kuralının geriye dönük olarak bu raporların configuration/run/report hash'lerini değiştirmesi reproducibility sözleşmesini bozar.

## Karar

- `BacktestExecutionPolicy`, strategy instrument kimliğiyle eşleşmesi zorunlu opsiyonel bir `InstrumentRules` snapshot'ı taşır.
- Snapshot price tick, quantity step, minimum quantity ve minimum notional değerlerinin tümünü içerir. Kısmi veya sıfır/negatif kural seti kabul edilmez.
- Alış fiyatları tick'e yukarı, satış fiyatları tick'e aşağı yuvarlanır. Miktarlar quantity step'e aşağı yuvarlanır; böylece maliyet ve gelir hesabı muhafazakârdır.
- Giriş bütçesi minimum quantity/notional eşiğini karşılamıyorsa fill üretilmez ve giriş target'ı kapanır.
- Bir çıkış kalan pozisyonu minimum kurallar altında satamıyorsa pozisyon açık ve exit target pending kalır; başarılı trade gibi raporlanmaz.
- Buy-and-hold benchmark aynı fiyat/miktar kurallarını kullanır. Giriş veya liquidation tradable değilse kısmi/iyimser benchmark raporu yerine fail-closed hata üretir.
- Instrument kuralları kullanılan run'lar `instrument-quantized-backtest-v1` configuration hash zarfına girer. Her dört değer ve instrument kimliği hash'e dahildir.
- Kuralsız policy legacy davranıştır; mevcut v1 ve v2 configuration/run/report hash hesapları aynen korunur.
- Research CLI dört opsiyonun tamamını birlikte kabul eder: `--tick-size`, `--quantity-step`, `--minimum-quantity`, `--minimum-notional`. Kısmi set reddedilir.
- Kural değerleri çalıştırmanın versioned artifact'ıdır. Eksik exchange metadata'sı tahmin edilmez veya başka bir alan minimum notional gibi yorumlanmaz.

## Sonuçlar

- Strateji ve benchmark aynı executable-unit sınırlarıyla karşılaştırılır.
- Yuvarlama sermaye allocation'ını aşamaz; kullanılmayan quote bakiye nakitte kalır.
- Minimum altı giriş ve satılamayan remainder görünür biçimde fail-closed kalır.
- Eski kanıtlar tekrar üretilebilir; yeni quantized sonuçlar ayrı configuration identity taşır.
- Bu model order-book queue position, market impact, cancel latency veya gerçek fee asset davranışını çözmez.

## Alternatifler

- Kuralları zorunlu yapıp eski raporları yeniden yazmak, kilitli kanıt kimliğini bozacağı için reddedildi.
- En yakın tick/lot değerine yuvarlamak, bazı alışlarda bütçeyi veya satışlarda mevcut miktarı aşabileceği için reddedildi.
- OKX `minSz` alanını minimum notional kabul etmek, alan base-quantity ifade ettiği için reddedildi.
- Kuralları manifest dışında tutmak, aynı configuration hash altında farklı finansal sonuç üreteceği için reddedildi.
