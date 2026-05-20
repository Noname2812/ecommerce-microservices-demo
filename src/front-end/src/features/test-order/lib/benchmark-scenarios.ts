import {
  buildPlaceOrderBody,
  buildValidShippingAddress,
  type PlaceOrderBody,
  type PlaceOrderLine,
  uuidV4,
} from "./place-order-api";
import {
  SEED_SELLER_ID,
  SEED_SELLER_NAME,
  benchmarkUserId,
  seedProductMeta,
} from "./place-order-seed";

export type BenchmarkScenarioKind =
  | "happy_path"
  | "inventory_contention"
  | "validation_failure";

export type BenchmarkPlannedOrder = {
  runId: string;
  scenario: BenchmarkScenarioKind;
  userId: string;
  label: string;
  body: PlaceOrderBody;
};

function lineFromSeed(productIndex: number, quantity: number): PlaceOrderLine {
  const m = seedProductMeta(productIndex);
  return {
    productId: m.productId,
    productName: m.productName,
    productSlug: m.productSlug,
    variantId: m.variantId,
    variantSku: m.variantSku,
    variantName: m.variantName,
    sellerId: SEED_SELLER_ID,
    sellerName: SEED_SELLER_NAME,
    unitPrice: m.unitPrice,
    quantity,
    discountAmount: 0,
    imageUrl: null,
  };
}

const VALIDATION_CASES: Array<{
  label: string;
  build: () => PlaceOrderBody;
}> = [
  {
    label: "Phone regex",
    build: () =>
      buildPlaceOrderBody({
        items: [lineFromSeed(1, 1)],
        shippingAddress: buildValidShippingAddress({ phone: "not-a-phone" }),
      }),
  },
  {
    label: "Qty > 100",
    build: () =>
      buildPlaceOrderBody({
        items: [lineFromSeed(2, 101)],
      }),
  },
  {
    label: "Bad idempotency key",
    build: () =>
      buildPlaceOrderBody({
        items: [lineFromSeed(3, 1)],
        idempotencyKey: "not-a-uuid",
      }),
  },
  {
    label: "Invalid email",
    build: () =>
      buildPlaceOrderBody({
        items: [lineFromSeed(4, 1)],
        customerEmail: "not-an-email",
      }),
  },
  {
    label: "Empty product name",
    build: () => {
      const line = lineFromSeed(5, 1);
      return buildPlaceOrderBody({
        items: [{ ...line, productName: "" }],
      });
    },
  },
];

/** Product 1: ~100 available — qty 40 × many users → inventory race. */
const CONTENTION_PRODUCT_INDEX = 1;
const CONTENTION_QTY_PER_ORDER = 40;

export function planBenchmarkOrders(params: {
  userCount: number;
  orderCount: number;
}): BenchmarkPlannedOrder[] {
  const { userCount, orderCount } = params;
  const users = Math.max(1, userCount);
  const total = Math.max(1, orderCount);

  const happyCount = Math.max(1, Math.round(total * 0.4));
  const contentionCount = Math.max(1, Math.round(total * 0.4));
  const validationCount = Math.max(0, total - happyCount - contentionCount);

  const orders: BenchmarkPlannedOrder[] = [];
  let seq = 0;

  for (let i = 0; i < happyCount && orders.length < total; i++) {
    const userIndex = seq % users;
    const productIndex = (seq % 10) + 1;
    const m = seedProductMeta(productIndex);
    orders.push({
      runId: uuidV4(),
      scenario: "happy_path",
      userId: benchmarkUserId(userIndex),
      label: `P${productIndex} ×1 (avail ${m.availableQty})`,
      body: buildPlaceOrderBody({
        items: [lineFromSeed(productIndex, 1)],
        customerNote: `Happy path #${seq + 1}`,
      }),
    });
    seq++;
  }

  for (let i = 0; i < contentionCount && orders.length < total; i++) {
    const userIndex = seq % users;
    const m = seedProductMeta(CONTENTION_PRODUCT_INDEX);
    orders.push({
      runId: uuidV4(),
      scenario: "inventory_contention",
      userId: benchmarkUserId(userIndex),
      label: `P${CONTENTION_PRODUCT_INDEX} ×${CONTENTION_QTY_PER_ORDER} (avail ${m.availableQty})`,
      body: buildPlaceOrderBody({
        items: [lineFromSeed(CONTENTION_PRODUCT_INDEX, CONTENTION_QTY_PER_ORDER)],
        customerNote: `Inventory race #${seq + 1}`,
      }),
    });
    seq++;
  }

  for (let i = 0; i < validationCount; i++) {
    const userIndex = seq % users;
    const v = VALIDATION_CASES[i % VALIDATION_CASES.length];
    orders.push({
      runId: uuidV4(),
      scenario: "validation_failure",
      userId: benchmarkUserId(userIndex),
      label: v.label,
      body: v.build(),
    });
    seq++;
  }

  return orders.slice(0, total);
}

export const SCENARIO_LABELS: Record<BenchmarkScenarioKind, string> = {
  happy_path: "Happy path",
  inventory_contention: "Inventory contention",
  validation_failure: "Validation failure",
};

export const SCENARIO_HINTS: Record<BenchmarkScenarioKind, string> = {
  happy_path: "Mỗi order khác product seed (1–10), qty=1, user fake khác nhau.",
  inventory_contention: `Cùng product ${CONTENTION_PRODUCT_INDEX}, qty=${CONTENTION_QTY_PER_ORDER}/order — tranh stock (~${seedProductMeta(CONTENTION_PRODUCT_INDEX).availableQty} available).`,
  validation_failure: "FluentValidation 400 — không vào saga.",
};
