import { useEffect, useRef, useState } from "react";
import axios from "axios";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { Button } from "@/shared/ui/button";
import { cn } from "@/shared/lib/utils";

type OrderStatusHistoryDto = {
  fromStatus: string | null;
  toStatus: string;
  note: string | null;
  createdAt: string;
};

type OrderDetailDto = {
  id: string;
  orderNumber: string;
  status: string;
  paymentStatus: string;
  subtotal: number;
  shippingFee: number;
  discountAmount: number;
  totalAmount: number;
  cancelledReason: string | null;
  createdAt: string;
  updatedAt: string;
  statusHistory: OrderStatusHistoryDto[];
};

type StepState = "done" | "active" | "pending" | "failed";

type Step = {
  key: string;
  label: string;
  hint: string;
};

const STEPS: Step[] = [
  { key: "placed",     label: "Placed",            hint: "Order saved, saga started" },
  { key: "reserving",  label: "Reserving stock",   hint: "Inventory + coupon (if any)" },
  { key: "confirmed",  label: "Confirmed",         hint: "Stock reserved" },
  { key: "awaiting",   label: "Awaiting payment",  hint: "Payment session created" },
  { key: "paid",       label: "Paid",              hint: "Payment completed" },
];

function deriveStepIndex(order: OrderDetailDto): number {
  if (order.status === "CANCELLED") return -1;
  if (order.paymentStatus === "PAID") return 4;
  if (order.paymentStatus === "AWAITING_PAYMENT") return 3;
  if (order.status === "CONFIRMED") return 2;
  if (order.status === "PENDING") return 1;
  return 0;
}

function stepState(index: number, current: number, isCancelled: boolean): StepState {
  if (isCancelled && index >= 2) return "failed";
  if (index < current) return "done";
  if (index === current) return "active";
  return "pending";
}

function isTerminal(order: OrderDetailDto): boolean {
  return order.status === "CANCELLED" || order.paymentStatus === "PAID";
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString();
  } catch {
    return iso;
  }
}

interface Props {
  orderId: string;
  userId: string;
  scope: "own" | "all";
  intervalMs?: number;
}

export function OrderStatusPoller({ orderId, userId, scope, intervalMs = 2000 }: Props) {
  const [order, setOrder] = useState<OrderDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [polling, setPolling] = useState(true);
  const [pollCount, setPollCount] = useState(0);
  const [lastPolledAt, setLastPolledAt] = useState<Date | null>(null);
  const cancelledRef = useRef(false);

  useEffect(() => {
    cancelledRef.current = false;
    setOrder(null);
    setError(null);
    setPolling(true);
    setPollCount(0);

    let timer: ReturnType<typeof setTimeout> | null = null;

    async function poll() {
      if (cancelledRef.current) return;
      try {
        const res = await axios.get<OrderDetailDto>(`/order-api/v1/orders/${orderId}`, {
          headers: {
            "X-User-Id": userId,
            "X-User-Roles": "customer",
            "X-Permission-Scope": scope,
          },
          validateStatus: () => true,
        });

        if (cancelledRef.current) return;
        setPollCount((c) => c + 1);
        setLastPolledAt(new Date());

        if (res.status >= 200 && res.status < 300) {
          setOrder(res.data);
          setError(null);
          if (isTerminal(res.data)) {
            setPolling(false);
            return;
          }
        } else {
          setError(`HTTP ${res.status}: ${JSON.stringify(res.data)}`);
        }
      } catch (e) {
        if (cancelledRef.current) return;
        setError(e instanceof Error ? e.message : String(e));
      }
      if (!cancelledRef.current) {
        timer = setTimeout(poll, intervalMs);
      }
    }

    poll();

    return () => {
      cancelledRef.current = true;
      if (timer) clearTimeout(timer);
    };
  }, [orderId, userId, scope, intervalMs]);

  const currentStep = order ? deriveStepIndex(order) : 0;
  const isCancelled = order?.status === "CANCELLED";

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>
            Order status —{" "}
            <code className="text-xs">{orderId.slice(0, 8)}…</code>
          </span>
          <span className="flex items-center gap-2 text-xs font-normal text-muted-foreground">
            {polling ? (
              <>
                <span className="inline-block h-2 w-2 animate-pulse rounded-full bg-green-500" />
                Polling…
              </>
            ) : (
              <>
                <span className="inline-block h-2 w-2 rounded-full bg-zinc-400" />
                Stopped
              </>
            )}
            <span>·</span>
            <span>{pollCount} polls</span>
            {lastPolledAt && (
              <>
                <span>·</span>
                <span>last {formatTime(lastPolledAt.toISOString())}</span>
              </>
            )}
            {!polling && (
              <Button size="sm" variant="ghost" className="ml-2 h-7 px-2 text-xs" onClick={() => setPolling(true)}>
                Resume?
              </Button>
            )}
          </span>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {error && (
          <div className="rounded-md border border-red-200 bg-red-50 p-2 text-xs text-red-700">{error}</div>
        )}

        {/* Stepper */}
        <ol className="grid grid-cols-5 gap-2">
          {STEPS.map((step, i) => {
            const state = stepState(i, currentStep, isCancelled);
            return (
              <li
                key={step.key}
                className={cn(
                  "flex flex-col items-start rounded-md border p-2",
                  state === "done"    && "border-green-300 bg-green-50",
                  state === "active"  && "border-blue-400 bg-blue-50 ring-2 ring-blue-200",
                  state === "pending" && "border-zinc-200 bg-zinc-50 text-muted-foreground",
                  state === "failed"  && "border-red-300 bg-red-50",
                )}
              >
                <div className="flex items-center gap-2 text-xs font-medium">
                  <span
                    className={cn(
                      "flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold",
                      state === "done"    && "bg-green-500 text-white",
                      state === "active"  && "bg-blue-500 text-white",
                      state === "pending" && "bg-zinc-300 text-zinc-700",
                      state === "failed"  && "bg-red-500 text-white",
                    )}
                  >
                    {state === "done" ? "✓" : state === "failed" ? "✕" : i + 1}
                  </span>
                  <span>{step.label}</span>
                </div>
                <p className="mt-1 text-[10px] leading-tight">{step.hint}</p>
              </li>
            );
          })}
        </ol>

        {/* Current snapshot */}
        {order && (
          <div className="grid grid-cols-2 gap-3 rounded-md border bg-card p-3 text-sm sm:grid-cols-4">
            <Field label="Order #" value={order.orderNumber} />
            <Field label="Status" value={<StatusBadge status={order.status} />} />
            <Field label="Payment" value={<StatusBadge status={order.paymentStatus} kind="payment" />} />
            <Field label="Total" value={order.totalAmount.toLocaleString()} />
            {order.cancelledReason && (
              <div className="col-span-full">
                <span className="text-xs font-medium text-muted-foreground">Cancel reason: </span>
                <span className="text-xs text-red-700">{order.cancelledReason}</span>
              </div>
            )}
          </div>
        )}

        {/* Timeline */}
        {order && order.statusHistory.length > 0 && (
          <div>
            <h3 className="mb-2 text-xs font-medium uppercase text-muted-foreground">Status timeline</h3>
            <ul className="space-y-1.5 border-l-2 border-zinc-200 pl-3">
              {order.statusHistory.map((h, i) => (
                <li key={i} className="relative text-xs">
                  <span className="absolute -left-[17px] top-1 h-2.5 w-2.5 rounded-full bg-blue-500 ring-2 ring-white" />
                  <div className="flex items-center gap-2">
                    {h.fromStatus && <span className="text-muted-foreground">{h.fromStatus}</span>}
                    {h.fromStatus && <span className="text-muted-foreground">→</span>}
                    <span className="font-medium">{h.toStatus}</span>
                    <span className="text-muted-foreground">· {formatTime(h.createdAt)}</span>
                  </div>
                  {h.note && <div className="text-muted-foreground">{h.note}</div>}
                </li>
              ))}
            </ul>
          </div>
        )}

        {!order && !error && (
          <div className="text-xs text-muted-foreground">Đang chờ poll đầu tiên…</div>
        )}
      </CardContent>
    </Card>
  );
}

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-[10px] uppercase text-muted-foreground">{label}</div>
      <div className="text-sm font-medium">{value}</div>
    </div>
  );
}

function StatusBadge({ status, kind }: { status: string; kind?: "payment" }) {
  const color =
    status === "PAID"               ? "bg-green-100 text-green-800" :
    status === "AWAITING_PAYMENT"   ? "bg-amber-100 text-amber-800" :
    status === "CANCELLED"          ? "bg-red-100 text-red-800"     :
    status === "CONFIRMED"          ? "bg-blue-100 text-blue-800"   :
    status === "PENDING"            ? "bg-zinc-100 text-zinc-800"   :
    kind === "payment"              ? "bg-zinc-100 text-zinc-800"   :
                                      "bg-zinc-100 text-zinc-800";
  return <span className={cn("rounded px-1.5 py-0.5 text-[11px] font-semibold", color)}>{status}</span>;
}
