# ADR-0008: İlk Borsa Olarak OKX TR Spot

**Durum:** Kabul edildi  
**Tarih:** 2026-07-26

## Bağlam

TradingBot kaldıraçsız Spot-only çalışacaktır ve ilk gerçek exchange adapter'ı için Türkiye'den kullanılabilir, demo ortamı bulunan, REST/WebSocket sözleşmeleri açıkça belgelenmiş bir borsa seçilmelidir. Market-data integrity çekirdeği sequence tabanlı recovery gerektirir.

## Karar

- İlk exchange adapter'ı `OKX TR` V5 API için geliştirilecektir.
- Yalnız `SPOT` instrument ve `tdMode=cash` emirleri desteklenecektir.
- İlk entegrasyon sırası public instrument metadata, REST order-book recovery, public WebSocket market data, demo private order/account ve en son kontrollü live canary olacaktır.
- Domain sembolü OKX adapter sınırında `BASE-QUOTE` biçimini kullanacaktır; ilk aday `BTC-USDT` olacaktır ve hesapta erişilebilirliği runtime metadata ile doğrulanacaktır.
- Order-book bütünlüğünde deprecated checksum kullanılmayacak; `seqId`/`prevSeqId` continuity esas alınacaktır.
- Production ve demo endpoint/credential'ları ayrılacak; live trading varsayılan olarak kapalı kalacaktır.
- API key withdrawal, margin veya derivatives izni taşımayacaktır.

## Sonuçlar

Olumlu:

- Türkiye'ye özel resmi V5 REST/WebSocket dokümantasyonu ve Demo Trading akışı vardır.
- REST order-book snapshot `seqId` taşıdığı için mevcut recovery cursor modeliyle uyumludur.
- Public market data credential gerektirmeden contract ve connectivity testlerine açılabilir.
- Spot `cash` modu kaldıraçsız ürün sınırını adapter seviyesinde açıkça uygular.

Bedeller:

- OKX'e özel sembol, hata kodu, rate-limit ve authentication mapping'i gerekir.
- Bölgesel endpoint ve ürün kullanılabilirliği runtime'da doğrulanmalıdır.
- İkinci bir borsa eklenene kadar exchange kaynaklı operasyonel bağımlılık vardır.

## Alternatifler

- Binance Spot: Teknik dokümantasyonu ve Spot Testnet'i güçlüdür; ikinci adapter adayıdır. Türkiye platformu ile global API özellik eşitliği ayrıca doğrulanmalıdır.
- Kraken/Coinbase: Güçlü API'lere rağmen ilk Türkiye operasyon kapsamı için ertelendi.
