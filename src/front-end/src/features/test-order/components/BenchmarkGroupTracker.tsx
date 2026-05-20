import { useMemo, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { Badge } from "@/shared/ui/badge";
import { Button } from "@/shared/ui/button";
import { cn } from "@/shared/lib/utils";
import type { OrderTicketStatusDto } from "./TicketStatusPoller";
import {
  SCENARIO_HINTS,
  SCENARIO_LABELS,
  type BenchmarkScenarioKind,
} from "../lib/benchmark-scenarios";

export type BenchmarkRunStatus =
  | "queued"
  | "posting"
  | "accepted"
  | "rejected"
  | "poll_error"
  | "terminal";

export type BenchmarkRun = {
  runId: string;
  scenario: BenchmarkScenarioKind;
  userId: string;
  label: string;
  status: BenchmarkRunStatus;
  httpStatus?: number;
  ticketId?: string | null;
  responseBody?: unknown;
  ticket?: OrderTicketStatusDto | null;
  pollError?: string;
};

function summarizeRuns(runs: BenchmarkRun[]) {
  const accepted = runs.filter((r) => r.status === "accepted" || r.status === "terminal");
  const rejected = runs.filter((r) => r.status === "rejected");
  const inFlight = runs.filter((r) => r.status === "accepted");
  const cancelled = runs.filter((r) => r.ticket?.status === "CANCELLED");
  const confirmed = runs.filter(
    (r) => r.ticket?.status === "CONFIRMED" && r.ticket?.paymentStatus === "PAID"
  );
  return {
    total: runs.length,
    accepted202: accepted.length,
    rejected: rejected.length,
    inFlight: inFlight.length,
    cancelled: cancelled.length,
    confirmed: confirmed.length,
  };
}

interface Props {
  scenario: BenchmarkScenarioKind;
  runs: BenchmarkRun[];
}

export function BenchmarkGroupTracker({ scenario, runs }: Props) {
  const [expanded, setExpanded] = useState(true);
  const stats = useMemo(() => summarizeRuns(runs), [runs]);

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle className="text-base">{SCENARIO_LABELS[scenario]}</CardTitle>
            <p className="mt-1 text-xs text-muted-foreground">{SCENARIO_HINTS[scenario]}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="outline">{stats.total} orders</Badge>
            <Badge className="bg-green-100 text-green-800">{stats.accepted202} accepted</Badge>
            <Badge className="bg-red-100 text-red-800">{stats.rejected} rejected</Badge>
            {stats.inFlight > 0 && (
              <Badge className="bg-blue-100 text-blue-800">{stats.inFlight} polling</Badge>
            )}
            {stats.cancelled > 0 && (
              <Badge className="bg-amber-100 text-amber-900">{stats.cancelled} cancelled</Badge>
            )}
            {stats.confirmed > 0 && (
              <Badge className="bg-emerald-100 text-emerald-900">{stats.confirmed} paid</Badge>
            )}
            <Button
              size="sm"
              variant="ghost"
              className="h-7 text-xs"
              onClick={() => setExpanded((e) => !e)}
            >
              {expanded ? "Thu gọn" : "Chi tiết"}
            </Button>
          </div>
        </div>
      </CardHeader>
      {expanded && (
        <CardContent>
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-xs">
              <thead className="bg-muted/50">
                <tr className="text-left">
                  <th className="p-2">#</th>
                  <th className="p-2">User</th>
                  <th className="p-2">Case</th>
                  <th className="p-2">POST</th>
                  <th className="p-2">Ticket / saga</th>
                </tr>
              </thead>
              <tbody>
                {runs.map((run, i) => (
                  <tr key={run.runId} className="border-t">
                    <td className="p-2 text-muted-foreground">{i + 1}</td>
                    <td className="p-2 font-mono text-[10px]">{run.userId.slice(0, 8)}…</td>
                    <td className="p-2">{run.label}</td>
                    <td className="p-2">
                      <PostBadge run={run} />
                    </td>
                    <td className="p-2">
                      <TicketCell run={run} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      )}
    </Card>
  );
}

function PostBadge({ run }: { run: BenchmarkRun }) {
  if (run.status === "queued" || run.status === "posting") {
    return <span className="text-muted-foreground">…</span>;
  }
  const ok = run.httpStatus !== undefined && run.httpStatus >= 200 && run.httpStatus < 300;
  return (
    <span
      className={cn(
        "rounded px-1.5 py-0.5 font-semibold",
        ok ? "bg-green-100 text-green-800" : "bg-red-100 text-red-800"
      )}
    >
      {run.httpStatus ?? "?"}
    </span>
  );
}

function TicketCell({ run }: { run: BenchmarkRun }) {
  if (run.pollError) {
    return <span className="text-red-600">{run.pollError}</span>;
  }
  if (!run.ticketId) {
    if (run.status === "rejected" && run.responseBody) {
      const msg =
        typeof run.responseBody === "object" && run.responseBody !== null
          ? JSON.stringify(run.responseBody).slice(0, 120)
          : String(run.responseBody);
      return <span className="text-red-700">{msg}</span>;
    }
    return <span className="text-muted-foreground">—</span>;
  }
  const t = run.ticket;
  if (!t) {
    return (
      <span className="text-muted-foreground">
        <code className="text-[10px]">{run.ticketId.slice(0, 8)}…</code> polling…
      </span>
    );
  }
  return (
    <div className="space-y-0.5">
      <div>
        <span className="font-medium">{t.status}</span>
        {t.paymentStatus && (
          <span className="ml-1 text-muted-foreground">/ {t.paymentStatus}</span>
        )}
      </div>
      {t.cancelledReason && <div className="text-red-700">{t.cancelledReason}</div>}
      {t.orderId && (
        <code className="text-[10px] text-muted-foreground">{t.orderId.slice(0, 8)}…</code>
      )}
    </div>
  );
}
