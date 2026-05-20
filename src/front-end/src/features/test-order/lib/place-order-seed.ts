/** Aligns with Catalog + Inventory seed (product index 1..10). */
export const SEED_SELLER_ID = "BBBBBBBB-BBBB-4BBB-8BBB-BBBBBBBBBBBB";
export const SEED_SELLER_NAME = "UrbanX Seed Seller";

export function seedProductId(n: number): string {
  return `${n.toString(16).padStart(8, "0")}-0000-4000-8000-000000000001`;
}

export function seedVariantId(n: number): string {
  return `${n.toString(16).padStart(8, "0")}-0000-4000-8000-000000000002`;
}

/** quantity_on_hand - quantity_reserved for seed line n (1..10). */
export function seedAvailableQty(n: number): number {
  const i = n - 1;
  return 100 - i - i;
}

export function seedProductMeta(n: number) {
  const price = 100_000 + n * 10_000;
  return {
    productId: seedProductId(n),
    variantId: seedVariantId(n),
    productName: `Điện thoại Product ${n}`,
    productSlug: `dien-thoai-product-${n}`,
    variantSku: `SEED-SKU-${n.toString().padStart(2, "0")}`,
    variantName: `Variant ${n}`,
    unitPrice: price,
    availableQty: seedAvailableQty(n),
  };
}

/** Stable fake buyer IDs for benchmark (not Identity users). */
export function benchmarkUserId(index: number): string {
  const hex = (index & 0xff).toString(16).padStart(2, "0");
  return `11111111-1111-4111-8111-0000000000${hex}`;
}

export const DEFAULT_BUYER_ID = "11111111-1111-4111-8111-111111111111";
