/**
 * Stress / load test: Catalog product search (GET .../products?q=...)
 *
 * Prerequisites: https://k6.io/docs/get-started/installation/
 *
 * Optimized for: VPS 4 vCPU / 8 GB RAM
 * Recommended k6 run command:
 *   k6 run --out json=results.json stress-test-search-product.k6.js
 *
 * ── EXECUTOR MODES ────────────────────────────────────────────────────────────
 *
 *   vus   (default) – ramping virtual users, good for warm-up / baseline
 *   rps             – constant arrival rate (RPS), good for SLA validation
 *   soak            – long-running moderate load, good for memory-leak detection
 *   spike           – sudden burst, good for auto-scaling / recovery testing
 *   stress          – push until failure, good for finding the breaking point
 *
 * ── EXAMPLES ──────────────────────────────────────────────────────────────────
 *
 *   # Default ramp-VU scenario
 *   k6 run stress-test-search-product.k6.js
 *
 *   # Custom base URL
 *   k6 run -e BASE_URL=http://localhost:5025 stress-test-search-product.k6.js
 *
 *   # Fixed-RPS mode (80 RPS for 3 minutes)
 *   k6 run -e EXECUTOR=rps -e TARGET_RPS=80 -e DURATION=3m stress-test-search-product.k6.js
 *
 *   # Soak test (50 VUs for 10 minutes)
 *   k6 run -e EXECUTOR=soak -e SOAK_VUS=50 -e DURATION=10m stress-test-search-product.k6.js
 *
 *   # Spike test
 *   k6 run -e EXECUTOR=spike stress-test-search-product.k6.js
 *
 *   # Stress test (find the breaking point)
 *   k6 run -e EXECUTOR=stress stress-test-search-product.k6.js
 */

import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend, Counter } from "k6/metrics";

// ── Environment ───────────────────────────────────────────────────────────────

const BASE_URL = (__ENV.BASE_URL || "http://localhost:5025").replace(/\/$/, "");
const EXECUTOR = (__ENV.EXECUTOR || "vus").toLowerCase();
const DURATION = __ENV.DURATION || "3m";

// ── Search query pool ─────────────────────────────────────────────────────────
// Thêm / bớt từ khoá tuỳ theo dữ liệu thực tế của bạn.
// Query sẽ được chọn ngẫu nhiên mỗi iteration để mô phỏng hành vi thực tế.

const SEARCH_QUERIES = [
  "thoai",
  "dien thoai",
  "samsung",
  "iphone",
  "laptop",
  "tai nghe",
  "sac du phong",
  "apple",
  "xiaomi",
  "oppo",
  "man hinh",
  "chuot",
  "ban phim",
  "o cung",
  "ram",
];

function randomQuery() {
  return SEARCH_QUERIES[Math.floor(Math.random() * SEARCH_QUERIES.length)];
}

function buildSearchPath(q) {
  return `/api/v1/catalog/products?q=${encodeURIComponent(q)}&page=1&pageSize=20&sort=relevance`;
}

// ── Custom metrics ─────────────────────────────────────────────────────────────

const searchErrorRate = new Rate("search_error_rate");
const searchDuration  = new Trend("search_duration_ms", true);
const searchRequests  = new Counter("search_requests_total");

// ── Shared request params ─────────────────────────────────────────────────────

const PARAMS = {
  tags:    { name: "SearchProducts" },
  timeout: "10s",
  headers: {
    "Accept":          "application/json",
    "Accept-Encoding": "gzip, deflate",
    // "Authorization": `Bearer ${__ENV.API_TOKEN}`,
  },
};

// ── Core search function ──────────────────────────────────────────────────────

export function doSearch() {
  const q    = randomQuery();
  const path = buildSearchPath(q);
  const res  = http.get(`${BASE_URL}${path}`, PARAMS);

  searchRequests.add(1);
  searchDuration.add(res.timings.duration);

  const ok = check(res, {
    "status is 200":          (r) => r.status === 200,
    "response time < 2000ms": (r) => r.timings.duration < 2000,
    "body is not empty":      (r) => r.body && r.body.length > 0,
  });

  searchErrorRate.add(!ok);

  if (!ok && res.status !== 0) {
    console.error(
      `[FAIL] q="${q}" HTTP ${res.status} | ${res.timings.duration.toFixed(0)}ms | body=${String(res.body).slice(0, 300)}`
    );
  }
}

// ── Scenario configurations ────────────────────────────────────────────────────
//
// Tuned for VPS 4 vCPU / 8 GB RAM.
//
// Rule of thumb so k6 itself doesn't starve the system under test:
//   - Max concurrent VUs: ≤ 150  (k6 goroutines are cheap but still use RAM/CPU)
//   - Max RPS target:     ≤ 100  (conservative starting point for a 4-core box)
//   - Soak VUs:          ≤  50  (safe for long-running baseline detection)
//   - Spike peak VUs:    ≤ 200  (short burst only, ~1 min hold max)
//   - Stress peak VUs:   ≤ 250  (intentional overload; stop when errors climb)

const SCENARIOS = {

  // ── 1. VU ramp (default) ────────────────────────────────────────────────────
  vus: {
    scenarios: {
      ramp_vus: {
        executor:  "ramping-vus",
        startVUs:  0,
        stages: [
          { duration: "30s", target: 10  },  // warm-up
          { duration: "1m",  target: 50  },  // moderate load
          { duration: "1m",  target: 100 },  // high load
          { duration: "1m",  target: 150 },  // sustained peak
          { duration: "30s", target: 0   },  // cool-down
        ],
        gracefulRampDown: "10s",
        exec: "doSearch",
      },
    },
    thresholds: {
      http_req_failed:                          ["rate<0.05"],
      http_req_duration:                        ["p(95)<2000"],
      "http_req_duration{name:SearchProducts}": ["p(99)<3000"],
      search_error_rate:                        ["rate<0.05"],
      search_duration_ms:                       ["p(95)<2000", "p(99)<3000"],
    },
  },

  // ── 2. Constant RPS ─────────────────────────────────────────────────────────
  rps: {
    scenarios: {
      fixed_rps: {
        executor:        "constant-arrival-rate",
        rate:            Number(__ENV.TARGET_RPS || 80),
        timeUnit:        "1s",
        duration:        DURATION,
        preAllocatedVUs: Number(__ENV.PRE_VUS || 50),
        maxVUs:          Number(__ENV.MAX_VUS  || 150),
        exec:            "doSearch",
      },
    },
    thresholds: {
      http_req_failed:    ["rate<0.05"],
      http_req_duration:  ["p(95)<2000"],
      search_error_rate:  ["rate<0.05"],
      search_duration_ms: ["p(95)<2000", "p(99)<3000"],
      dropped_iterations: ["count<50"],
    },
  },

  // ── 3. Soak test ────────────────────────────────────────────────────────────
  soak: {
    scenarios: {
      soak_test: {
        executor:  "ramping-vus",
        startVUs:  0,
        stages: [
          { duration: "1m",    target: Number(__ENV.SOAK_VUS || 50) },
          { duration: DURATION || "10m", target: Number(__ENV.SOAK_VUS || 50) },
          { duration: "30s",   target: 0 },
        ],
        gracefulRampDown: "15s",
        exec: "doSearch",
      },
    },
    thresholds: {
      http_req_failed:    ["rate<0.02"],
      http_req_duration:  ["p(95)<1500"],
      search_error_rate:  ["rate<0.02"],
    },
  },

  // ── 4. Spike test ───────────────────────────────────────────────────────────
  spike: {
    scenarios: {
      spike_test: {
        executor: "ramping-vus",
        startVUs: 0,
        stages: [
          { duration: "20s", target: 10  },  // baseline
          { duration: "10s", target: 200 },  // spike
          { duration: "1m",  target: 200 },  // hold
          { duration: "10s", target: 10  },  // recover
          { duration: "30s", target: 10  },  // verify recovery
          { duration: "10s", target: 0   },
        ],
        gracefulRampDown: "5s",
        exec: "doSearch",
      },
    },
    thresholds: {
      http_req_failed:   ["rate<0.10"],
      http_req_duration: ["p(95)<5000"],
    },
  },

  // ── 5. Stress test (find the breaking point) ───────────────────────────────
  stress: {
    scenarios: {
      stress_test: {
        executor: "ramping-vus",
        startVUs: 0,
        stages: [
          { duration: "30s", target: 25  },
          { duration: "1m",  target: 75  },
          { duration: "1m",  target: 150 },
          { duration: "1m",  target: 200 },
          { duration: "1m",  target: 250 },  // most APIs on a 4-core VPS break before this
          { duration: "30s", target: 0   },
        ],
        gracefulRampDown: "10s",
        exec: "doSearch",
      },
    },
    thresholds: {
      // Intentionally loose – we want to SEE the failure, not abort early.
      http_req_failed:   ["rate<0.50"],
      http_req_duration: ["p(95)<10000"],
    },
  },
};

// ── Export the chosen scenario ────────────────────────────────────────────────

export const options = SCENARIOS[EXECUTOR] ?? SCENARIOS["vus"];

// ── Default export ────────────────────────────────────────────────────────────

export default function () {
  doSearch();
  sleep(0.1);
}