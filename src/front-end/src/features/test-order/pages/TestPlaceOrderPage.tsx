import { useState } from "react";
import axios, { AxiosError } from "axios";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { Label } from "@/shared/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { TicketStatusPoller } from "@/features/test-order/components/TicketStatusPoller";
import { PlaceOrderBenchmarkPanel } from "@/features/test-order/components/PlaceOrderBenchmarkPanel";

const SEED_PRODUCT_ID = "00000001-0000-4000-8000-000000000001";
const SEED_VARIANT_ID = "00000001-0000-4000-8000-000000000002";
const SEED_SELLER_ID = "BBBBBBBB-BBBB-4BBB-8BBB-BBBBBBBBBBBB";
const SEED_PRODUCT_NAME = "Điện thoại Product 1";
const SEED_PRODUCT_SLUG = "dien-thoai-product-1";
const SEED_SELLER_NAME = "UrbanX Seed Seller";
const SEED_VARIANT_SKU = "SEED-SKU-01";
const SEED_VARIANT_NAME = "Variant 1";
const SEED_UNIT_PRICE = 110_000;
const SEED_VARIANT_VERSION = 1;

function uuidV4(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

function parseTicketIdFromResponse(
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

type CallResult =
  | { kind: "idle" }
  | { kind: "loading" }
  | {
      kind: "ok";
      status: number;
      body: unknown;
      headers: Record<string, string>;
      ticketId: string | null;
      requestBody: unknown;
      requestHeaders: Record<string, string>;
    }
  | {
      kind: "err";
      status?: number;
      body: unknown;
      message: string;
      requestBody: unknown;
      requestHeaders: Record<string, string>;
    };

export function TestPlaceOrderPage() {
  const [userId, setUserId] = useState("11111111-1111-4111-8111-111111111111");
  const [scope, setScope] = useState<"own" | "all">("own");
  const [quantity, setQuantity] = useState(2);
  const [version, setVersion] = useState(SEED_VARIANT_VERSION);
  const [unitPrice, setUnitPrice] = useState(SEED_UNIT_PRICE);
  const [result, setResult] = useState<CallResult>({ kind: "idle" });
  const [trackedTicketId, setTrackedTicketId] = useState<string | null>(null);
  const [manualTicketId, setManualTicketId] = useState("");

  async function handlePlaceOrder() {
    setResult({ kind: "loading" });

    const idempotencyKey = uuidV4();
    const capturedAt = new Date().toISOString();

    const body = {
      shippingAddress: {
        fullName: "Nguyen Van A",
        phone: "+84901234567",
        address: "123 Le Van Luong",
        ward: "Phuong Nhan Chinh",
        district: "thanhxuan",
        city: "hanoi",
        province: "Ha Noi",
        country: "VN",
        zipCode: "100000",
      },
      shippingFee: 25000,
      couponCode: null,
      customerNote: "Đặt thử từ UI mock",
      idempotencyKey,
      pricingSnapshot: { capturedAt },
      items: [
        {
          productId: SEED_PRODUCT_ID,
          productName: SEED_PRODUCT_NAME,
          productSlug: SEED_PRODUCT_SLUG,
          variantId: SEED_VARIANT_ID,
          variantSku: SEED_VARIANT_SKU,
          variantName: SEED_VARIANT_NAME,
          sellerId: SEED_SELLER_ID,
          sellerName: SEED_SELLER_NAME,
          unitPrice,
          quantity,
          discountAmount: 0,
          imageUrl: null,
          version,
        },
      ],
      customerEmail: "buyer@example.com",
    };

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      "X-User-Id": userId,
      "X-User-Roles": "customer",
      "X-Permission-Scope": scope,
      "Idempotency-Key": idempotencyKey,
    };

    try {
      const res = await axios.post("/order-api/v1/orders", body, {
        headers,
        validateStatus: () => true,
      });

      const flatHeaders = Object.fromEntries(
        Object.entries(res.headers).map(([k, v]) => [k, String(v)])
      );

      if (res.status >= 200 && res.status < 300) {
        const ticketId = parseTicketIdFromResponse(res.status, res.data, flatHeaders);
        setResult({
          kind: "ok",
          status: res.status,
          body: res.data,
          headers: flatHeaders,
          ticketId,
          requestBody: body,
          requestHeaders: headers,
        });
        if (ticketId) setTrackedTicketId(ticketId);
      } else {
        setResult({
          kind: "err",
          status: res.status,
          body: res.data,
          message: `HTTP ${res.status}`,
          requestBody: body,
          requestHeaders: headers,
        });
      }
    } catch (e) {
      const err = e as AxiosError;
      setResult({
        kind: "err",
        status: err.response?.status,
        body: err.response?.data ?? null,
        message: err.message,
        requestBody: body,
        requestHeaders: headers,
      });
    }
  }

  return (
    <div className="min-h-screen bg-background p-6">
      <div className="mx-auto max-w-5xl space-y-4">
        <div>
          <h1 className="text-2xl font-bold">Test — Place Order (async)</h1>
          <p className="text-sm text-muted-foreground">
            POST <code>/order-api/v1/orders</code> → <strong>202 Accepted</strong> +{" "}
            <code>ticketId</code>, sau đó poll{" "}
            <code>GET /order-api/v1/orders/ticket/{"{ticketId}"}</code>. Proxy → Order service (
            <code>localhost:5010</code>). Fake gateway headers, bỏ qua JWT.
          </p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Fake gateway headers</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="userId">X-User-Id (Guid)</Label>
              <Input id="userId" value={userId} onChange={(e) => setUserId(e.target.value)} />
            </div>
            <p className="text-xs text-muted-foreground">
              Phải khớp user có quyền đọc ticket (mặc định seed buyer). Scope <code>own</code> chỉ
              xem ticket của chính user.
            </p>

            <div className="space-y-1">
              <Label htmlFor="scope">X-Permission-Scope</Label>
              <select
                id="scope"
                value={scope}
                onChange={(e) => setScope(e.target.value as "own" | "all")}
                className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm"
              >
                <option value="own">own</option>
                <option value="all">all</option>
              </select>
            </div>

            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1">
                <Label htmlFor="qty">Quantity</Label>
                <Input
                  id="qty"
                  type="number"
                  min={1}
                  max={100}
                  value={quantity}
                  onChange={(e) => setQuantity(Number(e.target.value) || 1)}
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="unitPrice">Unit price</Label>
                <Input
                  id="unitPrice"
                  type="number"
                  min={0}
                  value={unitPrice}
                  onChange={(e) => setUnitPrice(Number(e.target.value) || 0)}
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="version">Variant version</Label>
                <Input
                  id="version"
                  type="number"
                  min={1}
                  value={version}
                  onChange={(e) => setVersion(Number(e.target.value) || 1)}
                />
              </div>
            </div>
            <p className="text-xs text-muted-foreground">
              Seed product n=1: price <code>110.000</code>, <code>RowVersion=1</code>. Đổi{" "}
              <code>version</code> ≠ 1 để test <code>Variant.VersionMismatch</code>; đổi{" "}
              <code>unitPrice</code> lệch &gt;1% để test <code>PRICE_MISMATCH</code>.
            </p>

            <div className="flex flex-wrap gap-2 pt-2">
              <Button onClick={handlePlaceOrder} disabled={result.kind === "loading"}>
                {result.kind === "loading" ? "Đang gửi..." : "Place Order"}
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setVersion(SEED_VARIANT_VERSION);
                  setUnitPrice(SEED_UNIT_PRICE);
                }}
                disabled={result.kind === "loading"}
              >
                Reset to seed
              </Button>
            </div>

            <p className="text-xs text-muted-foreground">
              Mỗi lần bấm sinh mới <code>Idempotency-Key</code> (header + body) và{" "}
              <code>pricingSnapshot.capturedAt</code> (UTC). Cần Catalog + Inventory seed và Order
              service đang chạy.
            </p>
          </CardContent>
        </Card>

        <PlaceOrderBenchmarkPanel scope={scope} />

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Track ticket</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap items-end gap-2">
            <div className="min-w-[200px] flex-1 space-y-1">
              <Label htmlFor="manualTicketId">Ticket ID (Guid)</Label>
              <Input
                id="manualTicketId"
                placeholder="paste ticketId from 202 response..."
                value={manualTicketId}
                onChange={(e) => setManualTicketId(e.target.value)}
              />
            </div>
            <Button
              variant="outline"
              onClick={() => manualTicketId && setTrackedTicketId(manualTicketId.trim())}
              disabled={!manualTicketId}
            >
              Track
            </Button>
            {trackedTicketId && (
              <Button variant="ghost" onClick={() => setTrackedTicketId(null)}>
                Clear
              </Button>
            )}
          </CardContent>
        </Card>

        {trackedTicketId && (
          <TicketStatusPoller
            key={trackedTicketId}
            ticketId={trackedTicketId}
            userId={userId}
            scope={scope}
          />
        )}

        {result.kind !== "idle" && result.kind !== "loading" && (
          <Card>
            <CardHeader>
              <CardTitle className="text-base">
                Response —{" "}
                <span className={result.kind === "ok" ? "text-green-600" : "text-red-600"}>
                  {result.kind === "ok" ? `OK ${result.status}` : `Error${result.status ? ` ${result.status}` : ""}`}
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {result.kind === "ok" && result.status === 202 && (
                <p className="text-sm text-muted-foreground">
                  Async accepted — dùng <code>ticketId</code> để poll. Location header (nếu có) trỏ
                  tới endpoint ticket.
                </p>
              )}
              {result.kind === "ok" && result.ticketId && (
                <div className="text-sm">
                  <span className="font-medium">Ticket ID:</span>{" "}
                  <code className="text-xs">{result.ticketId}</code>
                </div>
              )}
              {"status" in result && (
                <div className="text-sm">
                  <span className="font-medium">Status:</span> {result.status ?? "-"}
                </div>
              )}
              <div>
                <div className="mb-1 text-xs font-medium uppercase text-muted-foreground">
                  Response body
                </div>
                <pre className="overflow-auto rounded-md bg-muted p-3 text-xs">
                  {JSON.stringify(result.body, null, 2)}
                </pre>
              </div>
              <details>
                <summary className="cursor-pointer text-xs font-medium uppercase text-muted-foreground">
                  Request preview
                </summary>
                <div className="mt-2 space-y-2">
                  <pre className="overflow-auto rounded-md bg-muted p-3 text-xs">
                    {JSON.stringify(result.requestHeaders, null, 2)}
                  </pre>
                  <pre className="overflow-auto rounded-md bg-muted p-3 text-xs">
                    {JSON.stringify(result.requestBody, null, 2)}
                  </pre>
                </div>
              </details>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}
