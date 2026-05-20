import { useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { Label } from "@/shared/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { BenchmarkGroupTracker, type BenchmarkRun } from "./BenchmarkGroupTracker";
import type { OrderTicketStatusDto } from "./TicketStatusPoller";
import { postPlaceOrder } from "../lib/place-order-api";
import {
  planBenchmarkOrders,
  type BenchmarkScenarioKind,
} from "../lib/benchmark-scenarios";
import { benchmarkUserId } from "../lib/place-order-seed";

function isTerminalTicket(t: OrderTicketStatusDto): boolean {
  if (t.status === "CANCELLED") return true;
  return t.status === "CONFIRMED" && t.paymentStatus === "PAID";
}

interface Props {
  scope: "own" | "all";
}

export function PlaceOrderBenchmarkPanel({ scope }: Props) {
  const [userCount, setUserCount] = useState(6);
  const [orderCount, setOrderCount] = useState(15);
  const [runs, setRuns] = useState<BenchmarkRun[]>([]);
  const [running, setRunning] = useState(false);
  const [polling, setPolling] = useState(false);
  const cancelledRef = useRef(false);
  const runsRef = useRef(runs);
  runsRef.current = runs;

  const byScenario = useMemo(() => {
    const groups: Record<BenchmarkScenarioKind, BenchmarkRun[]> = {
      happy_path: [],
      inventory_contention: [],
      validation_failure: [],
    };
    for (const r of runs) groups[r.scenario].push(r);
    return groups;
  }, [runs]);

  const ticketPollKey = runs
    .filter((r) => r.ticketId && r.status !== "rejected" && (!r.ticket || !isTerminalTicket(r.ticket)))
    .map((r) => `${r.runId}:${r.ticketId}:${r.ticket?.status ?? ""}`)
    .join("|");

  useEffect(() => {
    if (!polling || !ticketPollKey) return;

    cancelledRef.current = false;
    let timer: ReturnType<typeof setTimeout> | null = null;

    async function pollAll() {
      if (cancelledRef.current) return;

      const pending = runsRef.current.filter(
        (r) =>
          r.ticketId && r.status !== "rejected" && (!r.ticket || !isTerminalTicket(r.ticket))
      );

      if (pending.length === 0) {
        setPolling(false);
        return;
      }

      const snapshots = await Promise.all(
        pending.map(async (run) => {
          try {
            const res = await axios.get<OrderTicketStatusDto>(
              `/order-api/v1/orders/ticket/${run.ticketId}`,
              {
                headers: {
                  "X-User-Id": run.userId,
                  "X-User-Roles": "customer",
                  "X-Permission-Scope": scope,
                },
                validateStatus: () => true,
              }
            );
            if (res.status >= 200 && res.status < 300) {
              return { runId: run.runId, ticket: res.data, pollError: undefined as string | undefined };
            }
            return { runId: run.runId, ticket: null, pollError: `HTTP ${res.status}` };
          } catch (e) {
            return {
              runId: run.runId,
              ticket: null,
              pollError: e instanceof Error ? e.message : String(e),
            };
          }
        })
      );

      if (cancelledRef.current) return;

      setRuns((prev) =>
        prev.map((r) => {
          const snap = snapshots.find((s) => s.runId === r.runId);
          if (!snap) return r;
          if (snap.pollError) {
            return { ...r, pollError: snap.pollError, status: "poll_error" as const };
          }
          if (!snap.ticket) return r;
          const terminal = isTerminalTicket(snap.ticket);
          return {
            ...r,
            ticket: snap.ticket,
            pollError: undefined,
            status: terminal ? ("terminal" as const) : ("accepted" as const),
          };
        })
      );

      if (!cancelledRef.current) {
        timer = setTimeout(pollAll, 2000);
      }
    }

    pollAll();

    return () => {
      cancelledRef.current = true;
      if (timer) clearTimeout(timer);
    };
  }, [polling, ticketPollKey, scope]);

  async function handleRunBenchmark() {
    const planned = planBenchmarkOrders({ userCount, orderCount });
    const initial: BenchmarkRun[] = planned.map((p) => ({
      runId: p.runId,
      scenario: p.scenario,
      userId: p.userId,
      label: p.label,
      status: "queued",
    }));
    setRuns(initial);
    setRunning(true);
    setPolling(false);

    setRuns((prev) => prev.map((r) => ({ ...r, status: "posting" as const })));

    const byUser = new Map<string, typeof planned>();
    for (const p of planned) {
      const list = byUser.get(p.userId) ?? [];
      list.push(p);
      byUser.set(p.userId, list);
    }

    async function postOne(p: (typeof planned)[number]) {
      try {
        const res = await postPlaceOrder(p.body, p.userId, scope);
        setRuns((prev) =>
          prev.map((r) => {
            if (r.runId !== p.runId) return r;
            if (res.httpStatus >= 200 && res.httpStatus < 300) {
              return {
                ...r,
                status: "accepted",
                httpStatus: res.httpStatus,
                ticketId: res.ticketId,
                responseBody: res.body,
              };
            }
            return {
              ...r,
              status: "rejected",
              httpStatus: res.httpStatus,
              responseBody: res.body,
            };
          })
        );
      } catch (e) {
        setRuns((prev) =>
          prev.map((r) =>
            r.runId === p.runId
              ? {
                  ...r,
                  status: "rejected",
                  responseBody: e instanceof Error ? e.message : String(e),
                }
              : r
          )
        );
      }
    }

    // Parallel across users; sequential per user (MaxNormalPendingPerUser=1).
    await Promise.all(
      [...byUser.values()].map(async (orders) => {
        for (const p of orders) {
          await postOne(p);
        }
      })
    );

    setRunning(false);
    setPolling(true);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Benchmark — fake load</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-xs text-muted-foreground">
          Phát đồng thời nhiều order với <code>X-User-Id</code> fake (
          <code>{benchmarkUserId(0).slice(0, 24)}…</code>). Mỗi user chỉ 1 pending order (
          <code>MaxNormalPendingPerUser=1</code>) — nên đặt số user ≥ số order trong nhóm
          contention. Chia ~40% happy / 40% inventory race / 20% validation.
        </p>

        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="benchUsers">Số user fake</Label>
            <Input
              id="benchUsers"
              type="number"
              min={1}
              max={64}
              value={userCount}
              onChange={(e) => setUserCount(Math.max(1, Number(e.target.value) || 1))}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="benchOrders">Tổng số order</Label>
            <Input
              id="benchOrders"
              type="number"
              min={3}
              max={60}
              value={orderCount}
              onChange={(e) => setOrderCount(Math.max(3, Number(e.target.value) || 3))}
            />
          </div>
        </div>

        <Button onClick={handleRunBenchmark} disabled={running}>
          {running ? "Đang bắn request…" : "Chạy benchmark"}
        </Button>

        {runs.length > 0 && (
          <div className="space-y-3">
            {(Object.keys(byScenario) as BenchmarkScenarioKind[]).map((scenario) =>
              byScenario[scenario].length > 0 ? (
                <BenchmarkGroupTracker key={scenario} scenario={scenario} runs={byScenario[scenario]} />
              ) : null
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
