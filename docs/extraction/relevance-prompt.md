Sen işçi ve emekçi direnişlerini kaydeden bir araştırma ekibine yardım ediyorsun (Direniş Takip). Aşağıdaki haberin
ekibin kaydedeceği bir **emek eylemini** anlatıp anlatmadığına karar ver. Yalnızca aşağıdaki JSON biçiminde yanıt ver;
JSON dışında hiçbir şey yazma.

## İlgili: üç koşul da sağlanıyor

1. **Eylemi yapanlar emekçiler ya da örgütleri.** İşçiler, memurlar, emekliler, işsizler ya da onların örgütleri:
   sendikalar, konfederasyonlar, meslek odaları ve meslek örgütleri (TTB, TMMOB, barolar, basın meslek örgütleri vb.),
   emek ve demokrasi platformları, işçi dayanışma ağları. Tek bir işçi de olabilir.
2. **Türkiye'de somut bir eylem var.** Eylem yapıldı, sürüyor ya da tarihi ve yeri belli olarak başlatılıyor: grev, iş
   bırakma, bir yerde yapılan basın açıklaması, yürüyüş, miting, anma, oturma eylemi, çadır kurma, süren bir direniş,
   imza kampanyası, işyerinde eylem, açlık grevi, çatıya çıkma vb. Bir sendikanın ya da meslek örgütünün bir yerde
   toplanarak yaptığı basın açıklaması da eylemdir. Eylemin sonucu (kazanım, anlaşma, işe iade), eyleme yönelik gözaltı
   ya da süren bir direnişin yeni bir günü de sayılır.
3. **Haber bu eylemi anlatıyor.** Eylem haberin konusu ya da haberde açıkça anlatılıyor. Bir işçiyle yapılan söyleşi ya
   da bir direniş hakkında yazılmış bir haber, aynı mücadelede yapılmış bir eylemi anlatıyorsa (geçmişte olsa bile) sayılır.

Eylemin konusu ücret, iş güvencesi, çalışma koşulları ya da sendikal haklar olmak zorunda değil: emek örgütlerinin
basın özgürlüğü, tutuklamalar, deprem, iklim, barış, Filistin gibi konulardaki eylemleri de sayılır.

## İlgili değil: açıkça

- Eylem Türkiye dışında (başka ülkelerdeki grevler ve eylemler).
- Hiç eylem anlatılmıyor: analiz ve araştırma haberleri, köşe yazıları, işçilerin sorunlarını, şikayetlerini ya da
  gördükleri baskıyı anlatan ama bir eylem anlatmayan haberler ve söyleşiler, iş kazası ya da iş cinayeti haberi (eylem
  yoksa), toplu sözleşme görüşmelerinin durumu (eylem yoksa).
- Okur ve işçi mektupları: bir eylemden söz etseler bile.
- Yalnızca açıklama: sosyal medya paylaşımı, video mesaj, yazılı açıklama; bir partinin, siyasetçinin ya da yetkilinin
  açıklaması ve ona gelen tepkiler, soru önergesi. Haberin asıl konusu bir siyasetçinin, partinin ya da yetkilinin bir
  eylem hakkında söyledikleriyse (eylemin kendisi değil) de ilgili değildir.
- Emekle ilgisi olmayan haberler: siyaset, kültür, spor, dış haberler, hava durumu, adliye haberleri vb.

## Belirsiz

Haberde **bir eylem geçiyor** ama yukarıdaki iki gruptan birine açıkça girmiyorsa `uncertain` yaz. Haberde hiçbir eylem
yoksa `uncertain` değil `not_relevant` yaz. Örneğin:
- Eylemi yapanların emekçi örgütü olup olmadığı belli değil (insan hakları dernekleri, öğrenciler, mahpuslar, aileler,
  karma gruplar).
- Bir eylem yapılıp yapılmadığı, ya da nerede yapıldığı belli değil.
- Bir işçinin ya da direnişin anlatıldığı, bir eylemin de geçtiği ama kısaca geçtiği haberler.
- Metin yarım ya da çok kısa.

Araştırmacılar belirsiz haberlere ayrıca bakar; ilgili bir haberi kaçırmak, fazladan bir haber okumaktan daha kötüdür.

## Yanıt biçimi

```json
{ "verdict": "relevant", "reason": "bir cümlelik gerekçe" }
```

## Haber

Kaynak: {{SOURCE}} · Tarih: {{DATE}} · Başlık: {{TITLE}}

{{TEXT}}
