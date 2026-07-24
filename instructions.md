# 🛡️ 100 Maddelik Yüksek Performanslı .NET 10 Trading Botu Savunma Anayasası

Bu doküman, geliştirilecek olan kripto para trading botunun canlı üretim (production) ortamında batmasını, donmasını veya bakiye kaybetmesini engellemek için uyulması zorunlu olan 100 mimari ve operasyonel kuralı içerir.

## 🌐 BÖLÜM 1: Ağ, WebSocket ve I/O Güvenliği (1 - 15)
1. **Network Jitter (Gecikme Dalgalanması):** Ağ gecikmesindeki milisaniyelik dalgalanmalar emrin yanlış fiyattan eşleşmesine sebep olur. Giriş seviyesinde dinamik ofset hesaplanmalıdır.
2. **WebSocket Buffer Overflow:** Yoğun piyasa anlarında TCP soket tamponu dolar. `System.Net.Sockets` tampon boyutları (ReceiveBufferSize) genişletilmelidir.
3. **DNS Resolution Lag (Çözümleme Tıkanması):** Borsa API adresinin DNS çözümlemesi milisaniyeler kaybettirebilir. IP adresleri lokalde önbelleğe alınmalıdır (DNS Caching).
4. **TLS/SSL Handshake Gecikmesi:** Her REST isteğinde yeniden el sıkışma (handshake) yapılmamalıdır; `HttpClient` üzerinden HTTP/2 veya Keep-Alive aktif tutulmalıdır.
5. **WebSocket Half-Open (Yarım Açık) Durumu:** Bağlantı kopsa bile işletim sistemi soketi açık sanabilir. TCP Keep-Alive paketleri işletim sistemi seviyesinde zorlanmalıdır.
6. **Borsa Bakım Modu Körlüğü:** Borsalar bakıma girdiğinde WebSocket veri göndermeyi keser ama bağlantıyı koparmaz. Bot, borsanın sistem statü API’sini her 30 saniyede bir doğrulamalıdır.
7. **Proxy/CDN Ön Bellek Tuzağı:** Aradaki Cloudflare gibi servisler bayat REST yanıtları dönebilir. Her REST isteğine benzersiz bir `timestamp` veya `nonce` parametresi eklenmelidir.
8. **IPv6 ve IPv4 Geçiş Gecikmesi:** Çift hat kullanan sunucularda ağ kartı kararsız kalabilir. Bot sadece en düşük gecikmeli (preferable IPv4) protokolü kullanmaya zorlanmalıdır.
9. **Reconnection Storm (Yeniden Bağlanma Fırtınası):** Bağlantı koptuğunda bot milisaniyede bir borsa sunucusuna vurmamalıdır. Üstel geri çekilme (`Exponential Backoff + Jitter`) kullanılmalıdır.
10. **Ağ Paket Kaybı (Packet Loss):** TCP paket kaçırdığında yeniden ister ve bu gecikme yaratır. Bot, veri paketlerinin bütünlüğünü sıra numaraları (`sequence numbers`) üzerinden doğrulamalıdır.
11. **Eş zamanlı WebSocket ve REST Tutarsızlığı:** WebSocket fiyatı ile REST sorgu sonucu çelişebilir. Zaman damgası (Timestamp) en büyük olan veri her zaman mutlak doğru kabul edilmelidir.
12. **Bölgesel Ağ Blokajı / Sansür:** ISP kaynaklı borsa IP'lerine anlık kısıtlama gelebilir. Yedek hat veya tünelleme (Proxy) altyapısı hazırda bekletilmelidir.
13. **Yarım Kalmış Yazma (Partial Writes):** Ağ üzerinden büyük bir JSON paketi gönderilirken akış kesilebilir. `PipeReader` ve `PipeWriter` kullanılarak parça parça (chunked) veri yönetimi zırhlandırılmalıdır.
14. **Borsa API Versiyon Değişimi (Deprecation):** Borsalar API versiyonlarını günceller. Kod, versiyon değişikliklerini kolayca karşılayacak adaptör (`Adapter Pattern`) mimarisinde olmalıdır.
15. **Soket Sızıntısı (Socket Exhaustion):** Sürekli yeni `HttpClient` nesnesi türetmek işletim sistemindeki soketleri tüketir. Tek bir statik veya `IHttpClientFactory` ile yönetilen instance kullanılmalıdır.

## 💻 BÖLÜM 2: .NET Eş Zamanlılık ve Bellek Yönetimi (16 - 30)
16. **Task Başlatıp Unutma (Unhandled Task Exception):** Arka planda fırlatılan bir işlem hata alırsa ve `try-catch` yoksa uygulama sessizce çökebilir. Tüm arka plan görevleri izlenmelidir.
17. **İş Parçacığı Havuzu Açlığı (Thread Pool Starvation):** Senkron bloklar (`.Result` veya `.Wait()`) thread havuzunu tüketir. Kod tabanında tek bir satır bile blocking çağrı olmamalıdır.
18. **Büyük Nesne Yığını (LOH) Parçalanması:** 85.000 bayttan büyük diziler (Array) LOH'a gider ve GC tarafından taşınamaz, belleği parçalar. Büyük veri dizileri yerine `ArrayPool<T>` kullanılmalıdır.
19. **Closure İçinde Referans Sızıntısı:** Lambda ifadeleri içindeki yerel değişkenler fark edilmeden hafızada tutulabilir. `static anonymous functions` kullanılarak bellek sızıntıları önlenmelidir.
20. **Asenkron Kilitlenme (Deadlock):** Senkron ve asenkron dünyayı karıştırmak veya yanlış `lock` kullanımı botu dondurur. Sadece asenkron uyumlu `SemaphoreSlim` kullanılmalıdır.
21. **String Birleştirme (String Allocation) Maliyeti:** Saniyede binlerce kez `string + string` yapmak belleği havaya uçurur. Log veya mesaj üretiminde `ValueStringBuilder` veya `StringInterpolation` kullanılmalıdır.
22. **ConcurrentDictionary Yanılsaması:** `GetOrAdd` metodu thread-safe'tir ama değer üreten fabrika fonksiyonu (factory) iki kez çalışabilir. Çift işlem riskine karşı dikkatli olunmalıdır.
23. **CancellationToken İhmali:** İptal edilen bir emrin veya kapanan botun thread'leri arka planda çalışmaya devam etmemelidir. Her asenkron metoda `CancellationToken` geçilmelidir.
24. **Boks Etme (Boxing/Unboxing) Maliyeti:** `object` tipine değer tipi (struct) atamak bellekte yük yaratır. Generic yapılar (`where T : struct`) zorunlu tutulmalıdır.
25. **Yanlış ValueTask Kullanımı:** Bir `ValueTask` nesnesi iki kez `await` edilemez veya üzerinde `.Result` çağrılamaz. Kurallara tam uyulmalıdır.
26. **Olay Sızıntısı (Event Subscription Leak):** Bir sınıftan event dinleyip abonelikten (`-=`) çıkmamak, o sınıfın GC tarafından temizlenmesini engeller. Zayıf referanslar (`WeakEventManager`) tercih edilmelidir.
27. **Bellek Parçalanması (Pinned Memory Fragmentation):** Sabitlenen (pinned) nesneler GC'nin hareket alanını kısıtlar. Bellek yönetiminde `NativeMemory` veya `Unmanaged` alanlar çok kritik yerlerde dikkatli kullanılmalıdır.
28. **Yanlış Singleton Durum Yönetimi:** Bağımlılık enjeksiyonunda (DI) `Singleton` kaydedilen sınıfların içinde `Scoped` veriler tutulmamalıdır; state bozulur.
29. **Büyük Dosya Okuma Çökmesi:** Geçmiş verileri test ederken 5 GB'lık CSV dosyasını tek seferde `File.ReadAllLines` ile RAM'e yüklemek sunucuyu patlatır. `Streams` ve satır satır okuma zorunludur.
30. **AsyncLocal Veri Kayması:** Thread'ler arası taşınan bağlam verileri (`AsyncLocal`) derin asenkron çağrılarda kontrolsüz modifiye edilirse alt görevlerde veri bozulmasına yol açar.

## 🧮 BÖLÜM 3: Finansal Matematik ve Veri Doğruluğu (31 - 45)
31. **Fiyat Adımı (Price Tick Size) İhlali:** Borsa fiyatın sadece belirli adımların katı olmasını ister. Adım filtresi (`PriceFilter`) borsa API'sinden dinamik okunup uygulanmalıdır.
32. **Miktar Adımı (Lot Size) İhlali:** Minimum lot kurallarına uyulmalıdır. Lot boyutu kurallara göre aşağı yuvarlanmalıdır (`Floor`).
33. **Kademeli Emir Azalımı (Order Size Decay):** Kasa eridikçe emir boyutu minimum limitin (`MinNotional` - Örn: 5 dolar) altına düşebilir. Bot bu durumu önceden sezip işlemi engellemelidir.
34. **Görünmez Komisyon Kaybı:** Brüt kâr hesaplanırken borsanın her iki yönde alacağı komisyonlar hesaptan düşülmelidir. Kod matematiksel formüle komisyon çarpanını eklemelidir.
35. **Mum Verisi Eksikliği (Hole in History):** WebSocket anlık koptuğunda arada kalan mum verisi kaybolur. İndikatörler yanlış hesaplanır. Bot her bağlantıda eksik mumları REST ile tamamlamalıdır (`Gap Filling`).
36. **Geleceği Görme (Look-Ahead Bias) Hatası:** Simülasyon yazarken kazara mevcut mumu hesaplarken bir sonraki mumun kapanış fiyatını koda sızdırmak backtesti kusursuz gösterir ama canlıda batırır.
37. **Zaman Damgası Taşması (Unix Epoch Overflow):** Milisaniye cinsinden zaman damgaları (Int64) saniyeye çevrilirken matematiksel bölme hataları yapılmamalıdır.
38. **Ortalama Maliyet (DCA) Matematiksel Çöküşü:** Sürekli düşen bir üründe matematiksel olarak sonsuza kadar maliyet düşürülemez; maksimum kademe sayısı (Max DCA Steps) sınırlandırılmalıdır.
39. **Yetersiz Başlangıç Mumu (Warm-up Period):** 200 periyotluk EMA hesaplamak için botun elinde en az 200 adet geçmiş mum olmalıdır. Yoksa ilk sinyaller tamamen hatalı üretilir.
40. **Ters İşlem Slipajı (Inverse Slippage):** Satış yaparken emir defterindeki derinlik azaldıkça fiyat daha da aşağı kayar. Matematik, derinliğin kümülatif toplamını hesaplamalıdır.
41. **Fiyat İğnesi (Spike) Koruması:** Borsada anlık sistemsel bir hata nedeniyle fiyat bir milisaniyeliğine sıfıra düşebilir. Bot, son fiyattan %10'dan fazla sapan ani hareketleri doğrulamadan işlem açmamalıdır.
42. **Kaldıraç Değişim Senkronizasyonu:** Vadeli işlemlerde kaldıraç oranı değiştirildiğinde borsanın bunu onaylaması milisaniyeler alır. Onay cevabı gelmeden emir gönderilmemelidir.
43. **Çapraz (Cross) ve İzole (Isolated) Marjin Karışıklığı:** Botun marjin tipini borsa tarafında yanlış set etmesi, tek bir pozisyon yüzünden tüm cüzdanın tasfiye (likidasyon) olmasına yol açar.
44. **Hacimsiz Parite (Low Liquidity) Tuzağı:** Bot, 24 saatlik hacmi çok düşük olan paritelerde işleme girerse pozisyondan kârla çıkamaz, spread arasında ezilir.
45. **Gerçeğe Aykırı Emir Gerçekleşme Süresi (Simülasyon):** Simülasyonda limit emrin anında gerçekleştiği varsayılır. Canlıda ise emir defterinde sıranın size gelmesi gerekir. Simülasyona gecikme simüle edilmelidir.

## 🛡️ BÖLÜM 4: Borsa API Sınırları ve Risk Yönetimi (46 - 60)
46. **IP Banlanma Eşiği (Rate Limit Score):** İsteklerin ağırlığı (Weight) izlenmelidir. Bot, borsa API weight yanıt başlıklarını (`X-MBX-USED-WEIGHT`) okuyup hızını dinamik kısmalıdır.
47. **Açık Emir Sayısı Limiti:** Bir borsada aynı anda maksimum açık limit emir tutulabilir. Bot gereksiz eski emirleri budamalıdır (`Order Garbage Collection`).
48. **Spam Emir Cezası (Cancel Ratio):** Çok fazla emir atıp saniyeler içinde iptal etmek borsalar tarafından cezalandırılır. İptal/Gerçekleşme oranı takip edilmelidir.
49. **Hesap Durdurulma (Account Freeze) Yönetimi:** Borsa hesabı KYC veya şüpheli işlem nedeniyle anlık dondurabilir. Bot, her işlem öncesi hesap statüsünü (`canTrade: true`) doğrulamalıdır.
50. **Yetersiz Bakiye (Margin Call) Yönetimi:** Başka bir paritedeki açık pozisyon marjini tüketirse, yeni açılacak temiz pozisyon bakiye yetersizliğinden reddedilir.
51. **Çılgın Piyasa Kaldıraç Kısıtlaması:** Borsalar yüksek volatilitede maksimum kaldıracı (Örn: 50x'ten 10x'e) anlık düşürebilir. Bot güncel maksimum kaldıraç sınırını sorgulamalıdır.
52. **Tek Coin Risk Yoğunlaşması (Asset Exposure):** Botun tüm sermayeyi aynı anda aynı korelasyona sahip 5 farklı altcoine (Örn: 5 adet Meme coin) dağıtması riski bölmez. Sektörel çeşitlendirme şarttır.
53. **Pozisyon Boyutu Üst Sınırı (Max Position Notional):** Borsalar hesap büyüklüğüne göre maksimum taşıyabileceğiniz pozisyon değerini sınırlar. Bu sınır geçilirse emir reddedilir.
54. **Ters Yönlü Emir Kilidi (Dual Position Mode Tutarsızlığı):** Borsa "Tek Yönlü" moddayken botun hem LONG hem SHORT açmaya çalışması API hatası fırlatır. Pozisyon modu (Hedge Mode vs One-Way Mode) kilitlenmelidir.
55. **Stop-Loss Emir Kaçırması (Stop Trigger Failure):** Piyasa o kadar hızlı düşer ki borsa senin tetikleme fiyatını atlar. Çözüm: Tetikleme emri (Trigger/Stop Market) borsa sunucusunda (Server-side) tutulmalıdır, lokalde değil.
56. **"Kendi Kendini Eşleştirme" (Self-Trade) Cezası:** Botun kendi açtığı satış emrini, yine botun başka bir modülü satın almamalıdır. Borsalar bunu piyasa manipülasyonu sayar ve hesabı kapatır.
57. **Birlikte Çalışabilirlik (Inter-bot Interference) Hatası:** Aynı hesapta iki farklı bot çalıştırmak birbirlerinin pozisyonlarını kapatmalarına neden olur. Her bot benzersiz bir `ClientOrderId` öneki (prefix) kullanmalıdır.
58. **Zorunlu Likidasyon Fiyatı Yakınlığı:** Giriş fiyatı likidasyon fiyatına çok yakınsa borsa emri daha en baştan reddeder. Marjin güvenlik mesafesi kodla ölçülmelidir.
59. **Gizli Fonlama Saati Yayılımı (Funding Spread):** Fonlama ödemesinden tam 1 saniye önce spread aşırı açılır. O saniyede işleme girmek baştan zarar yazdırır.
60. **Emir Güncelleme Yarışı (Order Modification Race):** Var olan emri iptal edip yenisini göndermek yerine, destekleyen borsalarda `Amend Order` (Emir Güncelleme) kullanılmalıdır; hız kazandırır.

### 🔒 BÖLÜM 5: Güvenlik, Kimlik Doğrulama ve Loglama (61 - 75)

61. **Log Dosyasına API Secret Sızması:** Yanlışlıkla tüm exception nesnesini loglamak, bağlantı dizesindeki şifreleri veya API anahtarlarını diske yazar. Log filtreleme maskesi (`Log Masking`) kurulmalıdır.
62. **İmza (HMAC-SHA256) Zaman Aşımı:** Borsaya gönderilen isteklerdeki şifreli imzanın geçerlilik süresi (recvWindow) genelde 5000ms'dir. Sunucu saati geri kalırsa tüm istekler reddedilir. `NTP` saat senkronizasyonu her dakika çalışmalıdır.
63. **Hafızadan API Key Çalma Riski:** API Secret string olarak RAM'de düz metin durursa, sunucuya sızan bir zararlı bellek dökümünden (Memory Dump) anahtarı çalar. `SecureString` veya .NET'in şifreli bellek yapıları kullanılmalıdır.
64. **Yetkisiz Telegram Komut Yönetimi:** Botu Telegram'dan yönetiyorsan (`/stop`, `/buy`), başkalarının bota mesaj atması engellenmelidir. Sadece senin benzersiz `ChatId` değerinden gelen komutlar işlenmelidir.
65. **Veritabanı Şifreleme Eksikliği:** Botun işlem geçmişini tuttuğu SQLite/PostgreSQL veritabanı şifrelenmelidir. Yoksa sunucu ele geçirildiğinde tüm ticari sırlar ve geçmiş dökülür.
66. **Geliştirme Ortamı (Dangling API Key) Hatası:** Canlı API anahtarlarını lokal bilgisayardaki test kodlarında unutmak ve kazara GitHub'a pushlamak tüm kasanın çalınmasına yol açar. `.env` veya `User Secrets` kullanılmalıdır.
67. **Log Dosyasının Aşırı Büyüyüp I/O'yu Kilitlemesi:** `Console.WriteLine` veya yoğun diske yazma işlemi senkron olduğunda CPU'yu darboğaza sokar. Loglama asenkron arka plan thread'ine (Serilog Asynchronous Rolling File) devredilmelidir.
68. **Siber Saldırı ile Botu Yanıltma (Man-in-the-Middle):** Sunucu ile borsa arasındaki trafiğin arasına girilmesini engellemek için SSL sertifika doğrulaması (`Certificate Pinning`) kod seviyesinde zorlanmalıdır.
69. **Zayıf SSH Portu ve Sunucu Ele Geçirilmesi:** Standart 22 portunu açık bırakmak bot sunucusunun brute-force ile ele geçirilmesine neden olur. SSH anahtarı zorunlu tutulmalı, şifre ile giriş kapatılmalıdır.
70. **Log Rotasyon Eksikliği (DOS):** Temizlenmeyen eski loglar yüzünden diski dolan Linux VPS kernel paniğe girer ve botu kapatır.
71. **Hata Bildirim Kısıtlaması (Alert Fatigue):** Bot hata aldığında Telegram'a saniyede 100 mesaj atarsa Telegram API botu geçici olarak engeller (Rate Limit). Bildirimler biriktirilip (Throttling / Batching) gönderilmelidir.
72. **Bağımlılık Paket Güvenliği (Nuget Vulnerability):** Projeye eklenen harici bir kütüphanede (Örn: Eski bir JSON parser) güvenlik açığı olması botun arka kapıdan hacklenmesine yol açar. Düzenli `dotnet list package --vulnerable` çalıştırılmalıdır.
73. **Çalışma Zamanı İzleme Eksikliği:** Bot çalışıyor görünebilir ama iç döngüde donmuştur. Dışarıdan bağımsız bir izleme sistemi (`Uptime Robot` veya watchdog script'i) botun durumunu (Health Check Endpoint) izlemelidir.
74. **Yedeksiz Telegram Bot Kanalı:** Bildirim kanalı kapandığında bot kör kalır. Discord Webhook veya SMS gibi alternatif bir acil durum kanal altyapısı hazır bulundurulmalıdır.
75. **Borsa Bağımlı Güvenlik Güncellemesi:** Borsalar güvenlik protokollerini güncelleyebilir (Örn: TLS 1.2'den TLS 1.3'e geçiş). Sunucu işletim sistemi güncel tutulmalıdır.

### 🧠 BÖLÜM 6: Algoritma Tasarımı ve Mantıksal Hatalar (76 - 90)

76. **Yapay Zeka / İndikatör Sinyal Gecikmesi (Lag):** RSI indikatörü ancak mum kapandığında kesinleşir. Mum kapanmadan önceki anlık hareketlerde sinyal üretmek botun sürekli yanlış kararlar vermesine sebep olur.
77. **"Trend Arkası" (Chasing Green Candles) Tuzağı:** Bot çok sert yükselmiş bir mumu görüp en tepeden LONG açmamalıdır. Algoritma aşırı alım bölgelerinde (`FOMO Koruması`) devreye girmelidir.
78. **Hatalı Stop-Loss Güncelleme (Trailing Stop Flaw):** Takip eden stop noktası fiyata çok yakın set edilirse, piyasanın doğal milisaniyelik nefes alma dalgalanmalarında bot erkenden elenir.
79. **Çelişen İndikatörler Paradoksu:** Botun çalışması için hem RSI, hem MACD, hem de Stochastic indikatörünün aynı anda onay vermesini beklemek botu kilitleyecektir. Sinyal motoru ağırlıklı puanlama sistemi (`Scoring Model`) kullanmalıdır.
80. **Yatay Piyasa Tespiti (Regime Switching) Eksikliği:** Trend botları yatay piyasada batar. Bot, piyasa yapısını (Trend vs Range) ayırt eden bir makro filtreye (Örn: Bollinger Band Genişliği veya ADX) sahip olmalıdır.
81. **Haber Akışı (News/Event) Körlüğü:** Faiz kararları veya büyük veri açıklamalarında teknik analiz tamamen devre dışı kalır. Bot, küresel ekonomik takvim saatlerinde (Örn: Fed faiz kararı günü saat 21:00) otomatik olarak **"Güvenli Liman / Bekleme"** moduna geçmelidir.
82. **"Karda Bekleyememe" (Early Profit Taking):** Bot %1 kâr gördüğü an korkup hemen çıkarsa ama stop olduğunda tam %2 zarar yazarsa, matematiksel olarak büyümesi imkansızdır. Kâr potansiyeli serbest bırakılmalıdır.
83. **Açgözlülük Döngüsü (Grid Trap):** Çok fazla kademe açarak ağ (Grid) tradingi yapan sistemler, tek yönlü sert bir trend kırılımında tüm nakdi tüketip marjin çağrısı alır.
84. **Hatalı Zaman Dilimi Mum Senkronu:** 1 saatlik mumların borsanın açılış saatine göre değil, sunucu saatine göre bölünmesi tüm geçmiş grafik verilerini canlı verilerden farklı hesaplatır.
85. **"Kör Nokta" (Black Swan) Strateji Eksikliği:** Piyasa tek saniyede %30 çökebilir. Botun lokal hafızasındaki stop çalışmayabilir. Borsaya emir gönderilirken eş zamanlı olarak koruyucu `Hard Stop Limit` emri borsa tablosuna işlenmelidir.
86. **Çift Yönlü Arbitraj Kitlenmesi:** İki borsa arası arbitraj yaparken, bir borsada alım gerçekleşip diğer borsada ağ yavaşlığı nedeniyle satışın gerçekleşememesi botu açıkta bırakır.
87. **Hatalı Matematiksel Kütüphane Güveni:** Harici indikatör kütüphanelerinin sınır durumları (Örn: Sıfıra bölünme hatası) kodun donmasına yol açabilir. Çıktılar `double.IsNaN` kontrolünden geçirilmelidir.
88. **Korelasyon Körü Portföy Dağılımı:** Bot BTC long açarken aynı anda ETH short açmamalıdır; iki işlem birbirini nötrler ve sadece borsaya boş yere komisyon ödenmiş olur.
89. **Geriye Dönük Optimizasyon İllüzyonu:** Geçmiş veride en iyi çalışan parametrelerin gelecekte de çalışacağını varsaymak. Stratejiler dinamik piyasa koşullarına göre kendini güncelleyebilmelidir.
90. **Hatalı Emir Statü Takibi (Execution State Machine Flaw):** Emrin durumu `PENDING_NEW` (Borsaya ulaştı ama işleniyor) aşamasındayken botun emri iptal etmeye çalışması durum yönetimini kilitler.

### 🚀 BÖLÜM 7: DevOps, Deployment ve Süreç Yönetimi (91 - 100)

91. **Canlıda Kod Güncelleme (Hot Swap) Felaketi:** Bot çalışırken ve içeride açık pozisyon varken sunucudaki .NET uygulamasını kapatıp yeni versiyonu ayağa kaldırmak açık pozisyonun sahipsiz kalmasına yol açar. Güncelleme sadece **"Sıfır Pozisyon"** anında yapılmalıdır.
92. **İşletim Sistemi Otomatik Güncelleme Çökmesi:** Linux/Windows sunucunun gece yarısı güvenlik güncellemesi yapıp kendi kendine restart atması botu kapatır. Otomatik yeniden başlatmalar kontrollü yönetilmelidir.
93. **Yedeksiz Güç ve Altyapı:** Tek bir bulut sağlayıcıya (Örn: Sadece tek bir yerel veri merkezi) güvenmek. Sunucu çökerse yedek sunucu (`Failover Instance`) başka bir coğrafi bölgede anında otomatik ayağa kalkmalıdır.
94. **Veritabanı Şişmesi ve Disk I/O Tıkanması:** Her saniye tik verisini (Tick-by-tick) diske yazmak 1 ayda SSD'yi eskitir ve okuma/yazma hızını düşürür. Veriler bellekte biriktirilip toplu (`Bulk Insert`) yazılmalıdır.
95. **Yetersiz Hata İzleme Ölçümü (Telemetry):** Botun o anki CPU, RAM ve Ağ kullanım metrikleri izlenmiyorsa, sistem yavaş yavaş darboğaza girer ve yazılımcının haberi olmaz. `Prometheus` ve `Grafana` entegrasyonu kurulmalıdır.
96. **"Graceful Shutdown" (Kibar Kapanış) Eksikliği:** Sunucu kapatılırken veya `SIGTERM` sinyali geldiğinde .NET Worker Service paldır küldür kapanmamalıdır; önce açık emirleri iptal etmeli, durumu diske kaydetmeli ve öyle kapanmalıdır.
97. **Sunucu Saat Sapması (Clock Drift): Sanal sunucuların (VM) saatleri zamanla mikrosaniyeler seviyesinde geri kalır. Chrony veya Systemd-timesyncd servisleri sunucuda aktif edilmelidir.
98. **Hatalı Ortam Değişkeni Yapılandırması (Environment Mix-up): Test ortamı (Testnet) ayarlarıyla canlı ortam (Production) ayarlarını karıştırıp canlı sunucuya test kodu deploy etmek büyük finansal kayıplara sebep olur. CI/CD pipeline'ları ayrılmalıdır.
99. **Yetersiz Acil Durum Butonu (Kill Switch): Piyasa çökerken botu uzaktan tek tuşla durdurup tüm borsalardaki her şeyi nakde geçirecek küresel bir acil durum mekanizması (Global Kill Switch) sisteme en baştan entegre edilmelidir.
100. **İnsan Körü Müdahale Tuzağı: Bot pozisyondayken borsa arayüzünü tarayıcıdan açıp manuel olarak o pozisyonu kapatırsan botun kafası karışır. Bot, harici insan müdahalelerini anında sezip kendi lokal durumunu güncelleyecek esneklikte olmalıdır.