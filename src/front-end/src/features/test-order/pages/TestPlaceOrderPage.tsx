import { useState } from "react";
import axios, { AxiosError } from "axios";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { Label } from "@/shared/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { OrderStatusPoller } from "@/features/test-order/components/OrderStatusPoller";

const SEED_PRODUCT_ID = "00000001-0000-4000-8000-000000000001";
const SEED_VARIANT_ID = "00000001-0000-4000-8000-000000000002";
const SEED_SELLER_ID = "BBBBBBBB-BBBB-4BBB-8BBB-BBBBBBBBBBBB";
const SEED_PRODUCT_NAME = "Điện thoại Product 1";
const SEED_PRODUCT_SLUG = "dien-thoai-product-1";
const SEED_SELLER_NAME = "UrbanX Seed Seller";
const SEED_VARIANT_SKU = "SEED-SKU-01";
const SEED_VARIANT_NAME = "Variant 1";
const SEED_UNIT_PRICE = 110_000;

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

type CallResult =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "ok"; status: number; body: unknown; headers: Record<string, string>; requestBody: unknown; requestHeaders: Record<string, string> }
  | { kind: "err"; status?: number; body: unknown; message: string; requestBody: unknown; requestHeaders: Record<string, string> };

export function TestPlaceOrderPage() {
  const [userId, setUserId] = useState("11111111-1111-4111-8111-111111111111");
  const [scope, setScope] = useState<"own" | "all">("own");
  const [quantity, setQuantity] = useState(2);
  const [result, setResult] = useState<CallResult>({ kind: "idle" });
  const [trackedOrderId, setTrackedOrderId] = useState<string | null>(null);
  const [manualOrderId, setManualOrderId] = useState("");

  async function handlePlaceOrder() {
    setResult({ kind: "loading" });

    const idempotencyHeader = uuidV4();
    const idempotencyBody = uuidV4();
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
      idempotencyKey: idempotencyBody,
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
          unitPrice: SEED_UNIT_PRICE,
          quantity,
          discountAmount: 0,
          imageUrl: null,
        },
      ],
      customerEmail: "buyer@example.com",
    };

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      "X-User-Id": userId,
      "X-User-Roles": "customer",
      "X-Permission-Scope": scope,
      "Idempotency-Key": idempotencyHeader,
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
        setResult({
          kind: "ok",
          status: res.status,
          body: res.data,
          headers: flatHeaders,
          requestBody: body,
          requestHeaders: headers,
        });
        if (typeof res.data === "string") {
          setTrackedOrderId(res.data);
        } else if (res.data && typeof res.data === "object" && "id" in (res.data as Record<string, unknown>)) {
          setTrackedOrderId(String((res.data as Record<string, unknown>).id));
        }
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
      <div className="mx-auto max-w-3xl space-y-4">
        <div>
          <h1 className="text-2xl font-bold">Test — Place Normal Order</h1>
          <p className="text-sm text-muted-foreground">
            Gọi thẳng vào Order service qua proxy <code>/order-api</code> → <code>http://localhost:5010</code>.
            Fake gateway headers, bỏ qua auth.
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

            <div className="space-y-1">
              <Label htmlFor="qty">Quantity (item seed n=1, price 110.000)</Label>
              <Input
                id="qty"
                type="number"
                min={1}
                max={100}
                value={quantity}
                onChange={(e) => setQuantity(Number(e.target.value) || 1)}
              />
            </div>

            <div className="pt-2">
              <Button onClick={handlePlaceOrder} disabled={result.kind === "loading"}>
                {result.kind === "loading" ? "Đang gửi..." : "Place Order"}
              </Button>
            </div>

            <p className="text-xs text-muted-foreground">
              Mỗi lần bấm sẽ sinh mới <code>Idempotency-Key</code> + body <code>idempotencyKey</code> (UUID v4)
              và <code>pricingSnapshot.capturedAt</code> = now (UTC).
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Track an existing order</CardTitle>
          </CardHeader>
          <CardContent className="flex items-end gap-2">
            <div className="flex-1 space-y-1">
              <Label htmlFor="manualOrderId">Order ID (Guid)</Label>
              <Input
                id="manualOrderId"
                placeholder="paste order id..."
                value={manualOrderId}
                onChange={(e) => setManualOrderId(e.target.value)}
              />
            </div>
            <Button
              variant="outline"
              onClick={() => manualOrderId && setTrackedOrderId(manualOrderId.trim())}
              disabled={!manualOrderId}
            >
              Track
            </Button>
            {trackedOrderId && (
              <Button variant="ghost" onClick={() => setTrackedOrderId(null)}>
                Clear
              </Button>
            )}
          </CardContent>
        </Card>

        {trackedOrderId && (
          <OrderStatusPoller
            key={trackedOrderId}
            orderId={trackedOrderId}
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
                  {result.kind === "ok" ? "OK" : `Error${result.status ? ` ${result.status}` : ""}`}
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {"status" in result && (
                <div className="text-sm">
                  <span className="font-medium">Status:</span> {result.status ?? "-"}
                </div>
              )}
              <div>
                <div className="mb-1 text-xs font-medium uppercase text-muted-foreground">Response body</div>
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
