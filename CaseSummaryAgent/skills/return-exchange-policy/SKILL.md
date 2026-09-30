---
name: return-exchange-policy
description: İade, değişim, hasarlı/kusurlu ürün ve kargo gecikmesi taleplerinde hangi aksiyonun önerileceğini belirleyen şirket politikası ve karar prosedürü.
---

# İade ve Değişim Politikası — Karar Prosedürü

Bu beceri, müşteri talebi için **önerilecek aksiyonu** belirlemek içindir. Aksiyonu sen uygulamazsın;
temsilciye ne yapması gerektiğini ve hangi politika maddesine dayandığını yazarsın.

## Adımlar
1. `get_order` ile siparişi al. `daysSinceDelivery`, `withinReturnWindow` ve `daysPastEstimatedDelivery` alanlarını kullan;
   tarih farkını kendin hesaplama.
2. Talep türünü belirle: bilgi talebi, hasarlı/kusurlu ürün, cayma (beğenmedim), kargo gecikmesi.
3. Aşağıdaki karar tablosunu uygula. Madde numaralarını gerekçede belirt.
4. Hasarlı ürün için değişim önerilecekse `get_product_stock` ile stok kontrolü yap.
5. Tablo yetmiyorsa ya da istisna söz konusuysa `references/policy.md` kaynağını oku.

## Karar tablosu (özet)
| Durum | Koşul | Önerilen aksiyon | Madde |
|---|---|---|---|
| Bilgi talebi | Sipariş kargoda, gecikme yok | Kargo durumu ve tahmini teslim tarihiyle bilgi ver | 1.1 |
| Hasarlı / kusurlu ürün | Teslimattan sonra ≤ 14 gün ve stok var | Ücretsiz değişim | 2.1 |
| Hasarlı / kusurlu ürün | ≤ 14 gün ve stok yok | Tam iade | 2.2 |
| Cayma (beğenmedim) | Teslimattan sonra ≤ 14 gün | İade (kargo müşteriye ait) | 3.1 |
| Cayma veya hasar | > 14 gün | Politika dışı; ret önerisi, istisna için yönetici onayı | 3.2 |
| Tutar ≥ 1.000 ₺ | Politika dışı istisna talebi | Yönetici onayı zorunlu | 3.3 |
| Kargo gecikmesi | Tahmini teslimden ≥ 3 gün geçmiş | Kargo firmasıyla inceleme başlat, müşteriyi bilgilendir | 4.1 |
