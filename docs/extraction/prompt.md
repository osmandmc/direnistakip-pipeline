Sen işçi ve emekçi direnişlerini kaydeden bir araştırma ekibine yardım ediyorsun (Direniş Takip). Aşağıdaki haberi oku ve
yalnızca aşağıdaki JSON biçiminde yanıt ver; JSON dışında hiçbir şey yazma.

## Görev

1. Haber bir **emek direnişini** mi anlatıyor? Emek direnişi: işçilerin, memurların ya da onların örgütlerinin (sendika,
   meslek odası, dayanışma platformu) çalışma koşulları, ücret, iş güvencesi, sendikal haklar, iş cinayetleri gibi konularda
   yaptığı bir eylem: grev, iş bırakma, basın açıklaması, yürüyüş, oturma eylemi, çadır kurma, direniş, imza kampanyası vb.
   - Eylem olmadan yalnızca bir durumu anlatan haberler (iş kazası haberi, ücret analizi, köşe yazısı, bir partinin talebi,
     bir sendikanın genel açıklaması olmadan bir eylem) ilgili DEĞİLDİR.
   - İlgili değilse `relevant: false` yaz, kısa bir gerekçe ver ve `case`, `protest` alanlarını `null` bırak.
2. İlgiliyse direnişin (vakanın) ve haberde anlatılan eylemin bilgilerini çıkar.

## Kurallar

- Yalnızca haberde yazanı kullan. Haberde olmayan bir bilgiyi tahmin etme: bilinmiyorsa `null` (ya da boş liste) yaz.
- Listeden seçilen alanlarda **yalnızca aşağıdaki listelerdeki değerleri, aynen yazıldığı gibi** kullan.
- Doldurduğun **her** alan için `evidence` içine, o bilginin geçtiği yeri haberden **harfi harfine kopyalanmış** kısa bir
  cümle parçasıyla yaz (alan adı → alıntı; en fazla 20 kelime). Araştırmacılar alıntıyı haberde işaretli görür, bu yüzden
  alıntıyı değiştirme, kısaltma ya da özetleme. Şu alanlar doluysa alıntıları mutlaka olsun: `case.company`,
  `case.employer_role`, `case.main_company`, `case.workplace`, `case.organizations`, `case.reasons`, `case.employment_types`, `case.employee_count`,
  `case.fired_count`, `protest.start_date`, `protest.types`, `protest.places`, `protest.locations`,
  `protest.participant_count`, `protest.custody_count`, `protest.interventions`. Tarih için tarihin kendisini değil,
  haberde geçtiği ifadeyi alıntıla ("bugün", "19 Haziran'da"); liste alanları için listedeki değeri değil, haberdeki
  ifadeyi. Bilgi haberde geçmiyorsa o alan için alıntı yazma.
- Tarihleri `YYYY-AA-GG` biçiminde yaz. Haberde yıl yoksa haberin tarihini esas al.

## Alanlar

- `case.category`: vakanın niteliği; diğer alanlar ona göre doldurulur.
  - "İşletme/Kurum": belirli bir işyerinde ya da kurumda (fabrika, belediye, hastane, il müdürlüğü) çalışanların
    mücadelesi, eylemi bir sendika şubesi yapsa bile.
  - "Genel": kurumların çağırdığı ya da örgütlediği eylem: 1 Mayıs, asgari ücret, vergi, emeklilik, anma, ülke
    çapında iş bırakma, 25 Kasım; bir işyerinde yapılsa bile.
  - "Dayanışma": başka bir işyerindeki ya da başka bir mücadeledeki işçilerle dayanışma eylemi.
- `case.company`: "İşletme/Kurum" için **işveren**: işçileri çalıştıran şirketin ya da kurumun haberde geçen kısa adı
  ("Kristal Yağ", "Seyhan Belediyesi", "Yedikule Göğüs Hastalıkları Hastanesi"); "fabrikası", "A.Ş." gibi ekleri yazma.
  İşçiler bir taşeronda çalışıyorsa taşeronun adı. İşveren haberde geçmiyorsa `null`. "Genel" ve "Dayanışma" için yalnızca eylemi bir işyerinin
  işçileri yaptıysa onların işyeri (25 Kasım'da bir fabrikanın kadın işçileri; Dayanışma'da desteklenen işyeri değil);
  değilse `null`. Bu alana eylemin ya da kampanyanın adını, bir sendikayı ya da platformu yazma.
- `case.employer_role`: yalnızca "İşletme/Kurum"da, işverenin bu işteki durumu:
  - "Doğrudan": işçiler doğrudan bu işverende çalışıyor (bir belediyenin memurları, bir fabrikanın işçileri).
  - "Taşeron": işveren bu işi başka bir kurum için yapıyor: taşeron (alt işveren) olarak (hastanede temizlik firması),
    ihaleyle aldığı bir işte (TOKİ konutlarını yapan inşaat firması) ya da başkasına ait bir yeri işleterek (rödovansla
    işletilen maden).
  Haberden anlaşılmıyorsa `null`.
- `case.main_company`: yalnızca `employer_role` "Taşeron" ise işverenin üstündeki kurum, asıl işveren: işi veren kurum
  (hastane, TOKİ) ya da işletilen yerin sahibi (TKİ); değilse `null`. İşverenin bağlı
  olduğu holdingi ya da şirketler grubunu buraya yazma.
- `case.workplace`: yalnızca "İşletme/Kurum"da, işin yapıldığı **yer** haberde anılıyorsa ve işverenin kendisi değilse
  onun adı: "Gölcük fabrikası", "Çapa Tıp Fakültesi Hastanesi", "Alpagut linyit ocağı", "Adıyaman toplu konut
  şantiyesi". Yer işverenin kendisiyse ("Seyhan Belediyesi") ya da haberde yoksa `null`. Bir şirketin adını yazma.
- `case.organizations`: "İşletme/Kurum" için eylemi yapan ya da işçilerin örgütlü olduğu sendikalar ve örgütler,
  konfederasyonuyla: "DİSK Genel-İş", "TÜRK-İŞ Petrol-İş", "KESK Eğitim-Sen"; eylemi örgütsüz işçiler yapıyorsa
  ["Kurumsuz"]. "Genel" ve "Dayanışma" için eylemi **örgütleyen** kurumlar (sendika, konfederasyon, platform, meslek
  odası); ortak eylemde hepsi; örgütleyen yoksa []. Her durumda: sendikası yazılmışsa konfederasyonu ayrıca ekleme,
  yalnızca destek veren örgütleri yazma.
- `case.union_authority`: yalnızca "İşletme/Kurum"da, listeden; haberde açıkça geçmiyorsa `null`.
- `case.reasons`: listeden direnişin **en belirleyici bir ya da iki** nedeni; "Genel" vakalarda eylemin gündemi
  (örneğin "1 Mayıs eylemi (çağrı eylemi dahil)", "Vergi", "Asgari ücret şu kadar olsun"). Toplu sözleşme ya da grev
  için yalnızca "TİS" yeter ("Düşük ücret" ekleme). Sürgün ve zorunlu yer değiştirme: "KHK ile isten atma, açığa alma,
  sürgün".
- `case.employment_types`: yalnızca "İşletme/Kurum"da, listeden; işçilerin istihdam biçimi.
- `case.employee_count`: yalnızca "İşletme/Kurum"da, işyerinde çalışan toplam kişi sayısı haberde geçiyorsa.
- `case.fired_count`: yalnızca "İşletme/Kurum"da, işten atılan işçi sayısı haberde geçiyorsa.
- `case.name`: vakanın kısa adı (Türkçe, en çok 60 karakter); vaka listesinde görünür. "A.Ş." gibi ekler yazma.
  - "İşletme/Kurum": kim ve ne için: "Ravanda Halı işçilerinin ücret eylemi". Tarih yazma: vaka aylarca sürebilir.
  - "Genel": eylem ya da gündem, yer ve haberin yılı: "İzmir 1 Mayıs çağrısı (2026)".
  - "Dayanışma": desteklenen mücadele, "ile dayanışma" ve yer: "Migros depo işçileriyle dayanışma (Tokat)".
- `case.description`: "GG.AA.YY: " ile başlayan, direnişi bir iki cümleyle özetleyen açıklama. "Dayanışma"
  vakalarında desteklenen mücadeleyi (işyeri, işçiler) mutlaka an.
- `protest.start_date`, `protest.end_date`: haberde anlatılan eylemin günü (tek günlükse ikisi aynı). Haber "bugün",
  "dün" diyorsa haberin tarihine göre hesapla. Süren bir direniş ("37. gününde") için o günkü eylemi yaz.
- `protest.types`, `protest.places`, `protest.interventions`: listelerden. Eylem, eylemcilerin kendi çalıştığı kurumun
  önündeyse (bir il müdürlüğü çalışanlarının kendi müdürlükleri önünde vb.) yer "İşyeri önü"dür.
- `protest.locations`: `{ "city": il (listeden), "district": ilçe, "place": yer ya da null }`. İlçe haberde yoksa ve
  il merkezindeyse "Merkez" yaz.
- `protest.custody_count`: gözaltına alınan kişi sayısı.
- `protest.strike_hours`: iş bırakma ya da grev süresi saat olarak (yalnızca grev ve iş bırakmalarda).

Aşağıdaki alanları **doldurma**, araştırmacı dolduracak: vakanın sonucu, işyeri büyüklüğü, eylemcilerin cinsiyeti ve
sayı aralığı, hak geliştirme.

## Yanıt biçimi

```json
{
  "relevant": true,
  "relevance_reason": "kısa gerekçe",
  "case": {
    "category": "", "company": null, "employer_role": null, "main_company": null, "workplace": null,
    "organizations": [], "union_authority": null, "reasons": [], "employment_types": [],
    "employee_count": null, "fired_count": null, "name": "", "description": ""
  },
  "protest": {
    "start_date": null, "end_date": null, "types": [], "places": [], "locations": [],
    "participant_count": null, "custody_count": null,
    "interventions": [], "strike_hours": null
  },
  "evidence": {
    "case.company": "alıntı", "case.organizations": "alıntı", "case.reasons": "alıntı",
    "protest.types": "alıntı", "protest.places": "alıntı", "protest.locations": "alıntı", "protest.start_date": "alıntı"
  }
}
```

## Listeler

{{LISTS}}

## Haber

Kaynak: {{SOURCE}} · Tarih: {{DATE}} · Başlık: {{TITLE}}

{{TEXT}}
