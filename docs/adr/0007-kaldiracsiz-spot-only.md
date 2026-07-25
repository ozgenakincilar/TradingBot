# ADR-0007: Kaldıraçsız Spot-Only Trading

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

Futures ve margin ürünleri; kaldıraç, liquidation, funding, borçlanma, position mode ve daha yüksek operasyonel risk getirir. Projenin amacı sermayeyi koruyan, denetlenebilir ve teknik karmaşıklığı kontrollü bir otomasyon geliştirmektir. Kullanıcı kaldıraç kullanmak istememektedir.

## Karar

- TradingBot yalnızca kaldıraçsız Spot piyasada çalışacaktır.
- Futures, perpetual, options, cross/isolated margin ve borrowing desteklenmeyecektir.
- Short/negatif pozisyon oluşturulmayacaktır.
- Sell miktarı kullanılabilir Spot varlık bakiyesini aşamayacaktır.
- Yalnızca Spot market-data, account ve order endpoint'leri uygulanacaktır.
- Spot dışı instrument, endpoint veya credential yetkisi algılanırsa sistem trading-ready olmayacaktır.
- API key üzerinde withdrawal, futures, margin ve borrowing izinleri bulunmayacaktır.
- Koruyucu stop mümkün olduğunda borsanın Spot server-side emir yeteneğiyle uygulanacaktır.

## Sonuçlar

Olumlu:

- Liquidation ve kaldıraç kaynaklı sermaye kaybı riski kaldırılır.
- Funding, margin mode ve leverage senkronizasyon karmaşıklığı ortadan kalkar.
- Maksimum ekonomik exposure kullanılabilir sermaye ve eldeki varlıkla sınırlanır.
- Backtest ve reconciliation modeli sadeleşir.

Bedeller:

- Düşen piyasada açığa satışla getiri üretilemez.
- Sermaye verimliliği kaldıraçlı sistemlere göre daha düşüktür.
- Aylık `%10` gibi agresif hedefler daha seyrek gerçekleşebilir.
- Stop sonrası yeniden giriş ve nakitte bekleme stratejisi önem kazanır.

## Alternatifler

- Düşük kaldıraçlı Futures: Kullanıcı tercihi ve liquidation riski nedeniyle reddedildi.
- Isolated margin: Borçlanma ve operasyonel risk nedeniyle reddedildi.
- Spot + opsiyon koruması: İlk ürün kapsamını aşan karmaşıklık nedeniyle ertelendi.
