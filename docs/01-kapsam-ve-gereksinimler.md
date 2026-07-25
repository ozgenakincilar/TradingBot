# Kapsam ve Gereksinimler

**Durum:** Taslak  
**Hedef platform:** .NET 10  
**Başlangıç çalışma modu:** Paper trading

## 1. Amaç

Sistem; piyasa verisini güvenilir biçimde alan, strateji sinyallerini deterministik üreten, her emri merkezi risk kontrolünden geçiren ve işlemlerin tamamını denetlenebilir biçimde kaydeden bir trading otomasyonudur.

Kârlılık garanti değildir. Öncelik; sermayenin korunması, tutarlı durum yönetimi, tekrar üretilebilir test ve güvenli kapanmadır.

## 2. İlk sürüm kapsamı

- Tek borsa adaptörü; borsa ayrıca seçilecektir.
- Yalnızca kaldıraçsız Spot piyasa.
- Paper trading ve testnet desteği.
- REST snapshot ve WebSocket piyasa verisi.
- Mum verisi doğrulama ve gap filling.
- Tek stratejinin takılabilir şekilde çalıştırılması.
- Emir öncesi ve portföy seviyesinde risk kontrolleri.
- Emir yaşam döngüsü ve reconciliation.
- Sağlık kontrolü, metrik, yapılandırılmış log ve alarm.
- Graceful shutdown ve kill switch.
- Backtest ile live/paper yürütmenin aynı domain kurallarını kullanması.

## 3. İlk sürüm dışında

- Çoklu borsa arbitrajı.
- Yüksek frekanslı/kolokasyon gerektiren işlem.
- Kullanıcı fonu saklama veya transfer etme.
- Otomatik strateji üretimi ve kendi kendine model eğitimi.
- Mikroservis dağıtımı ve çok bölgeli active-active çalışma.
- Kullanıcıya yatırım tavsiyesi sunma.
- Futures, perpetual ve options ürünleri.
- Margin/cross/isolated hesap, borçlanma ve kaldıraç.
- Açığa satış veya negatif varlık pozisyonu.

## 4. Aktörler

| Aktör | Yetki ve sorumluluk |
|---|---|
| Operatör | Botu başlatır/durdurur, modu seçer, kill switch uygular |
| Strateji | Sinyal üretir; doğrudan emir gönderemez |
| Risk motoru | Emir niyetini onaylar, küçültür veya reddeder |
| Execution motoru | Onaylı emri borsaya iletir ve durumunu izler |
| Borsa | Piyasa, hesap ve emir verisinin dış kaynağıdır |
| İzleme sistemi | Sağlık ve SLO ihlallerini algılar |

## 5. Fonksiyonel gereksinimler

Kimlikler test ve izlenebilirlik için sabittir.

- **FR-001:** Sistem başlangıçta çalışma ortamını ve `Paper/Testnet/Live` modunu doğrulamalıdır.
- **FR-002:** Live mod, açık etkinleştirme ve geçerli risk yapılandırması olmadan başlayamamalıdır.
- **FR-003:** Piyasa olayları sembol, event time, receive time ve sequence bilgisiyle işlenmelidir.
- **FR-004:** Eksik veya sırası bozuk veri algılandığında ilgili sembolde sinyal üretimi durmalı ve snapshot ile onarılmalıdır.
- **FR-005:** Strateji yalnızca kapanmış mumlardan kesin sinyal üretmelidir.
- **FR-006:** Her emir niyeti tick size, lot size, min notional, bakiye ve risk limitlerinden geçmelidir.
- **FR-007:** Finansal hesaplamalarda `decimal`; zaman için UTC `DateTimeOffset`/Unix milliseconds kullanılmalıdır.
- **FR-008:** Emirler benzersiz ve idempotent `ClientOrderId` taşımalıdır.
- **FR-009:** Emir durumu açık bir state machine ile yönetilmelidir.
- **FR-010:** Kısmi gerçekleşme, red, timeout, cancel ve belirsiz sonuçlar işlenmelidir.
- **FR-011:** Yerel durum borsa durumu ile periyodik ve başlangıçta reconcile edilmelidir.
- **FR-012:** Manuel borsa müdahalesi algılandığında yerel durum güncellenmeli ve alarm üretilmelidir.
- **FR-013:** Stop koruması mümkün olduğunda server-side emir olarak kurulmalıdır.
- **FR-014:** Kill switch yeni emirleri durdurmalı; iptal/pozisyon kapatma davranışı yapılandırılmış politika ile uygulanmalıdır.
- **FR-015:** Sistem kapanırken yeni iş kabulünü durdurmalı, çalışan işleri sınır süre içinde tamamlamalı ve checkpoint yazmalıdır.
- **FR-016:** Backtest komisyon, slippage, latency ve likidite varsayımlarını içermelidir.
- **FR-017:** Her karar; correlation ID, strategy version, input snapshot ve risk sonucu ile denetlenebilmelidir.
- **FR-018:** Sistem yalnızca Spot endpoint ve instrument türlerini kabul etmeli; margin, futures veya leverage yapılandırmasında fail-fast davranmalıdır.
- **FR-019:** Satış miktarı kullanılabilir varlık bakiyesini aşmamalı ve pozisyon hiçbir zaman negatif olamamalıdır.
- **FR-020:** Aylık net `%10` stretch hedefi yeni risk oluşturmak için emir üretmemeli veya risk limitlerini dinamik yükseltmemelidir.

## 6. Kalite gereksinimleri

- **NFR-001 Güvenlik:** Secret hiçbir log, hata cevabı veya repoda bulunmamalıdır.
- **NFR-002 Güvenilirlik:** Tekrarlanan dış istekler idempotent olmalıdır.
- **NFR-003 Kullanılabilirlik:** Sağlık kontrolleri liveness, readiness ve dependency durumlarını ayırmalıdır.
- **NFR-004 Performans:** Kritik hot path bloklayan I/O ve `.Result`/`.Wait()` içermemelidir.
- **NFR-005 Dayanıklılık:** Reconnect exponential backoff + jitter kullanmalıdır.
- **NFR-006 Gözlemlenebilirlik:** Log, metric ve trace aynı correlation bağlamına sahip olmalıdır.
- **NFR-007 Test edilebilirlik:** Saat, rastgelelik, ağ ve persistence soyutlanmalıdır.
- **NFR-008 Taşınabilirlik:** Linux container birincil production hedefidir; Windows geliştirme desteklenir.
- **NFR-009 Sürdürülebilirlik:** Domain katmanı framework ve borsa SDK’sından bağımsızdır.
- **NFR-010 Kurtarılabilirlik:** Restart sonrası açık emir/pozisyonlar borsadan yeniden oluşturulabilir olmalıdır.

## 7. Açık ürün kararları

- İlk borsa hangisi?
- İlk strateji ve zaman dilimi nedir?
- Başlangıç sermayesi ve risk limitleri nedir?
- Kurulu SQL Server instance/sürümü, bağlantı yöntemi ve production lisanslama modeli nedir?
- Bildirim kanalları hangileridir?

Spot-only ürün kararı [ADR-0007](adr/0007-kaldiracsiz-spot-only.md) ile kapanmıştır. Kalan kararlar verilmeden gerçek borsa adaptörü veya live işlem geliştirilmez.
