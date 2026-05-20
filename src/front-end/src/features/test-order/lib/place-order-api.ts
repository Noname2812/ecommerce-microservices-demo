import axios from "axios";

export type PlaceOrderLine = {
  productId: string;
  productName: string;
  productSlug: string | null;
  variantId: string;
  variantSku: string;
  variantName: string | null;
  sellerId: string;
  sellerName: string;
  unitPrice: number;
  quantity: number;
  discountAmount: number;
  imageUrl: string | null;
};

export type PlaceOrderBody = {
  shippingAddress: {
    fullName: string;
    phone: string;
    address: string;
    ward: string | null;
    district: string;
    city: string;
    province: string | null;
    country: string;
    zipCode: string | null;
  };
  shippingFee: number;
  couponCode: string | null;
  customerNote: string | null;
  idempotencyKey: string;
  pricingSnapshot: { capturedAt: string };
  items: PlaceOrderLine[];
  customerEmail: string | null;
};

export function uuidV4(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export function parseTicketIdFromResponse(
  status: number,
  data: unknown,
  headers: Record<string, string>
): string | null {
  if (data && typeof data === "object" && "ticketId" in data) {
    const id = (data as { ticketId: unknown }).ticketId;
    if (typeof id === "string" && id) return id;
  }
  if (typeof data === "string" && data) return data;

  const location = headers.location ?? headers.Location;
  if (location) {
    const match = location.match(/\/ticket\/([0-9a-f-]{36})/i);
    if (match) return match[1];
  }

  if (status === 202 && data && typeof data === "object" && "id" in data) {
    return String((data as { id: unknown }).id);
  }

  return null;
}

export function buildValidShippingAddress(overrides?: Partial<PlaceOrderBody["shippingAddress"]>) {
  return {
    fullName: "Bench Nguyen",
    phone: "+84901234567",
    address: "123 Le Van Luong",
    ward: "Phuong Nhan Chinh",
    district: "thanhxuan",
    city: "hanoi",
    province: "Ha Noi",
    country: "VN",
    zipCode: "100000",
    ...overrides,
  };
}

export function buildPlaceOrderBody(params: {
  items: PlaceOrderLine[];
  idempotencyKey?: string;
  capturedAt?: string;
  shippingAddress?: PlaceOrderBody["shippingAddress"];
  customerEmail?: string | null;
  customerNote?: string | null;
}): PlaceOrderBody {
  return {
    shippingAddress: params.shippingAddress ?? buildValidShippingAddress(),
    shippingFee: 25_000,
    couponCode: null,
    customerNote: params.customerNote ?? "Benchmark run",
    idempotencyKey: params.idempotencyKey ?? uuidV4(),
    pricingSnapshot: { capturedAt: params.capturedAt ?? new Date().toISOString() },
    items: params.items,
    customerEmail: params.customerEmail ?? "bench@example.com",
  };
}

export async function postPlaceOrder(
  body: PlaceOrderBody,
  userId: string,
  scope: "own" | "all" = "own"
): Promise<{
  httpStatus: number;
  body: unknown;
  ticketId: string | null;
  headers: Record<string, string>;
}> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "X-User-Id": userId,
    "X-User-Roles": "customer",
    "X-Permission-Scope": scope,
    "Idempotency-Key": body.idempotencyKey,
  };

  const res = await axios.post("/order-api/v1/orders", body, {
    headers,
    validateStatus: () => true,
  });

  const flatHeaders = Object.fromEntries(
    Object.entries(res.headers).map(([k, v]) => [k, String(v)])
  );

  const ticketId =
    res.status >= 200 && res.status < 300
      ? parseTicketIdFromResponse(res.status, res.data, flatHeaders)
      : null;

  return { httpStatus: res.status, body: res.data, ticketId, headers: flatHeaders };
}
