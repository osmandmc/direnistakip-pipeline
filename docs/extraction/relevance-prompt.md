Sen işçi ve emekçi direnişlerini kaydeden bir araştırma ekibine yardım ediyorsun (Direniş Takip). Aşağıdaki haberin
ekibin kaydedeceği bir **emek direnişini** anlatıp anlatmadığına karar ver. Yalnızca aşağıdaki JSON biçiminde yanıt ver;
JSON dışında hiçbir şey yazma.

## Emek direnişi

İşçilerin, memurların, emeklilerin ya da onların örgütlerinin (sendika, meslek odası, dayanışma platformu) çalışma
koşulları, ücret, iş güvencesi, sendikal haklar, iş cinayetleri gibi konularda yaptığı **bir eylem**: grev, iş bırakma,
basın açıklaması, yürüyüş, miting, oturma eylemi, çadır kurma, süren bir direniş, imza kampanyası, işyerinde eylem vb.
Eylemin sonucu (kazanım, anlaşma, işe iade), eyleme yönelik gözaltı ya da süren bir direnişin yeni bir günü de sayılır.

## İlgili değil

- Eylem olmadan yalnızca bir durumu anlatan haberler: iş kazası ya da iş cinayeti haberi (eylem yoksa), ücret ve
  enflasyon analizi, işçilerle söyleşi, köşe yazısı, okur mektubu.
- Bir partinin, milletvekilinin ya da yetkilinin açıklaması, soru önergesi (işçi eylemi yoksa).
- Emekle ilgisi olmayan haberler: siyaset, kültür, spor, dış haberler, hava durumu vb.

## Karar

- `relevant`: haber bir emek direnişini anlatıyor.
- `not_relevant`: açıkça değil.
- `uncertain`: emin değilsen (eylem var mı belli değil, metin yarım, sınırda bir durum). **Şüphedeysen `not_relevant`
  yerine `uncertain` yaz**: ilgili bir haberi kaçırmak, fazladan bir haber okumaktan daha kötüdür.

## Yanıt biçimi

```json
{ "verdict": "relevant", "reason": "bir cümlelik gerekçe" }
```

## Haber

Kaynak: {{SOURCE}} · Tarih: {{DATE}} · Başlık: {{TITLE}}

{{TEXT}}
