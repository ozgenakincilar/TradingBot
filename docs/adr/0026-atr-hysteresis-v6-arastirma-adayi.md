# ADR-0026: ATR hysteresis v6 araştırma adayı

**Durum:** Kabul edildi

**Tarih:** 2026-07-27

## Bağlam

v5 yön filtresi trade ve maliyeti düşürmesine rağmen pozitif expectancy üretmedi. Attribution, 80 işlemin 79'unun sabit 30 bps EMA cross-down ile kapandığını ve küçük maliyet öncesi edge'in execution maliyetine dayanmadığını gösterdi. Aynı sabit bandı yeniden optimize etmek overfitting yaratır; yeni hipotez bandı piyasanın ölçülen volatilitesine boyutsal olarak bağlamalıdır.

## Karar

- v6, v5'ten çatallanır ve sabit 30 bps yerine signal `ATR(14) × 0,2` EMA bandı kullanır.
- ADX/DMI, EMA dönemleri, FOMO, exposure ve exit öncelikleri değişmez.
- Önceki ve güncel cross sınırları kendi nedensel ATR snapshot'larını kullanır.
- ATR alanları yalnız v6'da etkin olabilir; configuration schema `atr-hysteresis-v1` olur.
- Parametreler ve sekiz forward acceptance kapısı [v6 ön kaydında](../25-v6-atr-hysteresis-on-kaydi.md) sonuçtan önce kilitlenir.

## Sonuçlar

- Düşük volatilitede band daralır, yüksek volatilitede karesel execution maliyetinden bağımsız olarak strateji sinyal bandı genişler.
- Değişiklik kârlılık kanıtı değildir. Dinamik benchmark parity ve gerçek forward veri olmadan acceptance çalıştırılamaz.
- v1-v5 karar ve configuration kimlikleri korunur.

## Alternatifler

- Geçmiş yıllarda en iyi sabit bps taraması overfitting nedeniyle reddedildi.
- ATR ile birlikte trailing exit değiştirmek attribution'ı bozacağı için reddedildi.
- Güncel açık mumdan ATR üretmek look-ahead ve repaint riski nedeniyle reddedildi.
