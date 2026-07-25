# Performans ve Risk Hedefleri

**Durum:** Kabul edildi  
**Ürün sınırı:** Kaldıraçsız Spot

## 1. Hedef tanımı

TradingBot için aylık **net `%10` getiri bir stretch target** olarak tanımlanmıştır. Bu değer garanti, her ay doldurulması gereken kota veya pozisyon büyütme sinyali değildir.

Net getiri hesabı şunları düşer:

- Alış ve satış komisyonları.
- Spread.
- Gerçekleşen slippage.
- Operasyonel olarak doğrudan işleme yüklenen diğer maliyetler.

## 2. Risk hedefinden bağımsızlık

```text
Getiri hedefi karşılanmadı
  → risk limiti artırılmaz
  → pozisyon miktarı hedefe göre büyütülmez
  → işlem sıklığı zorlanmaz
  → kalite filtresi gevşetilmez
  → kaldıraç/margin etkinleştirilmez
```

Strateji yalnızca piyasa girdisi ve kabul edilmiş parametrelerinden sinyal üretir. “Ay sonuna kalan hedef” bir strategy veya risk-engine girdisi olamaz.

## 3. Başlangıç risk çerçevesi

| Ölçüt | Başlangıç sınırı | Tür |
|---|---:|---|
| Aylık net getiri | `%10` | Stretch target |
| Maksimum aylık drawdown | `%5` | Hard limit |
| Maksimum günlük kayıp | `%1` | Hard limit / trading halt |
| İşlem başına risk | `%0,25–%0,50` | Position-sizing sınırı |
| Kaldıraç | Yok | Değiştirilemez ürün sınırı |
| Negatif pozisyon | Yasak | Domain invariant |

Kesin başlangıç sermayesi ve strateji seçildikten sonra yüzde limitlerinin parasal karşılıkları ayrıca onaylanır.

## 4. Başarı değerlendirmesi

Tek bir ayın getirisi başarı kanıtı değildir. Production adaylığı için:

- En az 3–6 aylık paper/testnet gözlem penceresi.
- Komisyon ve slippage sonrası pozitif net expectancy.
- Profit factor hedefi en az `1.30`.
- Maksimum drawdown en fazla `%5`.
- Farklı trend/range ve yüksek volatilite dönemlerinde değerlendirme.
- Sonucun tek işlem, tek sembol veya tek piyasa rejimine bağımlı olmaması.
- Look-ahead bias içermeyen out-of-sample ve walk-forward sonuçları.

## 5. Halt ve yeniden etkinleştirme

- Günlük kayıp limiti aşılırsa gün sonuna kadar yeni exposure durur.
- Aylık drawdown limiti aşılırsa strateji otomatik olarak paper/shadow moda alınır.
- Risk ihlali sonrası otomatik risk yükseltme veya kaybı geri kazanma işlemi yapılamaz.
- Yeniden etkinleştirme root-cause incelemesi, reconciliation ve açık operatör onayı gerektirir.

## 6. Raporlama

Aylık rapor en az şu metrikleri içerir:

- Brüt ve net getiri.
- Komisyon, spread ve slippage maliyeti.
- Realized/unrealized PnL.
- Maksimum drawdown.
- Win rate, payoff ratio, profit factor ve expectancy.
- İşlem sayısı ve ortalama pozisyonda kalma süresi.
- Risk rejection ve trading-halt olayları.
- Benchmark karşılaştırması (örneğin buy-and-hold; bilgi amaçlı).

İlk backtest execution raporu gross/net return, realized PnL, fee/spread/slippage, net-liquidation value, drawdown, win rate, profit factor, expectancy ve ortalama holding time üretir. Aylık segmentasyon, benchmark, out-of-sample ve walk-forward raporları tamamlanana kadar `%10` stretch hedefi açısından başarı kanıtı sayılmaz.
