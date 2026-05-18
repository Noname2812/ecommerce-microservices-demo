import { useEffect, useRef, useState } from "react";
import axios from "axios";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { Button } from "@/shared/ui/button";
import { cn } from "@/shared/lib/utils";

export type OrderTicketStatusDto = {
  ticketId: string;
  status: string;
  orderId: string | null;
  paymentUrl: string | null;
  qrCodeUrl: string | null;
  paymentStatus: string | null;
  cancelledReason: string | null;
  paymentExpiresAt: string | null;
};

type StepState = "done" | "active" | "pending" | "failed";

type Step = {
  key: string;
  label: string;
  hint: string;
};

const STEPS: Step[] = [
  { key: "processing", label: "Processing", hint: "Saga: catalog, inventory, coupon" },
  { key: "payment", label: "Pending payment", hint: "Order created, pay via link" },
  { key: "confirmed", label: "Confirmed", hint: "Payment completed" },
];

function deriveStepIndex(ticket: OrderTicketStatusDto): number {
  if (ticket.status === "CANCELLED") return -1;
  if (ticket.paymentStatus === "PAID" || ticket.status === "CONFIRMED") return 2;
  if (
    ticket.status === "PENDING_PAYMENT" ||
    ticket.paymentStatus === "AWAITING_PAYMENT" ||
    ticket.paymentUrl
  ) {
    return 1;
  }
  return 0;
}

function stepState(index: number, current: number, isCancelled: boolean): StepState {
  if (isCancelled && index >= 1) return "failed";
  if (index < current) return "done";
  if (index === current) return "active";
  return "pending";
}

function isTerminal(ticket: OrderTicketStatusDto): boolean {
  if (ticket.status === "CANCELLED") return true;
  return ticket.status === "CONFIRMED" && ticket.paymentStatus === "PAID";
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString();
  } catch {
    return iso;
  }
}

interface Props {
  ticketId: string;
  userId: string;
  scope: "own" | "all";
  intervalMs?: number;
}

export function TicketStatusPoller({ ticketId, userId, scope, intervalMs = 2000 }: Props) {
  const [ticket, setTicket] = useState<OrderTicketStatusDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [polling, setPolling] = useState(true);
  const [pollCount, setPollCount] = useState(0);
  const [lastPolledAt, setLastPolledAt] = useState<Date | null>(null);
  const cancelledRef = useRef(false);

  useEffect(() => {
    cancelledRef.current = false;
    setTicket(null);
    setError(null);
    setPolling(true);
    setPollCount(0);

    let timer: ReturnType<typeof setTimeout> | null = null;

    async function poll() {
      if (cancelledRef.current) return;
      try {
        const res = await axios.get<OrderTicketStatusDto>(
          `/order-api/v1/orders/ticket/${ticketId}`,
          {
            headers: {
              "X-User-Id": userId,
              "X-User-Roles": "customer",
              "X-Permission-Scope": scope,
            },
            validateStatus: () => true,
          }
        );

        if (cancelledRef.current) return;
        setPollCount((c) => c + 1);
        setLastPolledAt(new Date());

        if (res.status >= 200 && res.status < 300) {
          setTicket(res.data);
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
  }, [ticketId, userId, scope, intervalMs]);

  const currentStep = ticket ? deriveStepIndex(ticket) : 0;
  const isCancelled = ticket?.status === "CANCELLED";

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>
            Ticket status —{" "}
            <code className="text-xs">{ticketId.slice(0, 8)}…</code>
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
              <Button
                size="sm"
                variant="ghost"
                className="ml-2 h-7 px-2 text-xs"
                onClick={() => setPolling(true)}
              >
                Resume
              </Button>
            )}
          </span>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {error && (
          <div className="rounded-md border border-red-200 bg-red-50 p-2 text-xs text-red-700">
            {error}
          </div>
        )}

        <ol className="grid grid-cols-3 gap-2">
          {STEPS.map((step, i) => {
            const state = stepState(i, currentStep, isCancelled);
            return (
              <li
                key={step.key}
                className={cn(
                  "flex flex-col items-start rounded-md border p-2",
                  state === "done" && "border-green-300 bg-green-50",
                  state === "active" && "border-blue-400 bg-blue-50 ring-2 ring-blue-200",
                  state === "pending" && "border-zinc-200 bg-zinc-50 text-muted-foreground",
                  state === "failed" && "border-red-300 bg-red-50"
                )}
              >
                <div className="flex items-center gap-2 text-xs font-medium">
                  <span
                    className={cn(
                      "flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold",
                      state === "done" && "bg-green-500 text-white",
                      state === "active" && "bg-blue-500 text-white",
                      state === "pending" && "bg-zinc-300 text-zinc-700",
                      state === "failed" && "bg-red-500 text-white"
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

        {ticket && (
          <div className="space-y-3 rounded-md border bg-card p-3 text-sm">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Field label="Status" value={<StatusBadge status={ticket.status} />} />
              <Field
                label="Payment"
                value={
                  ticket.paymentStatus ? (
                    <StatusBadge status={ticket.paymentStatus} kind="payment" />
                  ) : (
                    "—"
                  )
                }
              />
              <Field
                label="Order ID"
                value={
                  ticket.orderId ? (
                    <code className="text-[10px]">{ticket.orderId}</code>
                  ) : (
                    "—"
                  )
                }
              />
              {ticket.paymentExpiresAt && (
                <Field label="Pay before" value={formatTime(ticket.paymentExpiresAt)} />
              )}
            </div>

            {ticket.paymentUrl && (
              <div className="space-y-1">
                <div className="text-[10px] uppercase text-muted-foreground">Payment URL</div>
                <a
                  href={ticket.paymentUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="break-all text-xs text-blue-600 underline"
                >
                  {ticket.paymentUrl}
                </a>
              </div>
            )}

            {ticket.qrCodeUrl && (
              <div className="space-y-1">
                <div className="text-[10px] uppercase text-muted-foreground">QR code</div>
                <a
                  href={ticket.qrCodeUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="break-all text-xs text-blue-600 underline"
                >
                  {ticket.qrCodeUrl}
                </a>
              </div>
            )}

            {ticket.cancelledReason && (
              <div>
                <span className="text-xs font-medium text-muted-foreground">Cancel reason: </span>
                <span className="text-xs text-red-700">{ticket.cancelledReason}</span>
              </div>
            )}
          </div>
        )}

        {!ticket && !error && (
          <div className="text-xs text-muted-foreground">Đang chờ poll đầu tiên…</div>
        )}

        <details className="text-xs">
          <summary className="cursor-pointer font-medium uppercase text-muted-foreground">
            Raw ticket JSON
          </summary>
          <pre className="mt-2 overflow-auto rounded-md bg-muted p-3 text-[10px]">
            {ticket ? JSON.stringify(ticket, null, 2) : "—"}
          </pre>
        </details>
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
    status === "PAID" || status === "CONFIRMED"
      ? "bg-green-100 text-green-800"
      : status === "AWAITING_PAYMENT" || status === "PENDING_PAYMENT"
        ? "bg-amber-100 text-amber-800"
        : status === "CANCELLED"
          ? "bg-red-100 text-red-800"
          : status === "PROCESSING"
            ? "bg-blue-100 text-blue-800"
            : kind === "payment"
              ? "bg-zinc-100 text-zinc-800"
              : "bg-zinc-100 text-zinc-800";
  return (
    <span className={cn("rounded px-1.5 py-0.5 text-[11px] font-semibold", color)}>{status}</span>
  );
}
