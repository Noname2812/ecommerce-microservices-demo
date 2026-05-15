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
  // Điện thoại
  "dien thoai", "smartphone", "iphone", "samsung", "xiaomi", "oppo", "vivo", "realme", "nokia", "motorola",
  "iphone 15", "iphone 14", "iphone 13", "samsung galaxy", "samsung a55", "samsung s24", "xiaomi 14",
  "oppo reno", "vivo v30", "realme 12", "dien thoai gaming", "dien thoai 5g", "dien thoai gia re",
  "dien thoai android", "dien thoai cam ung", "may bo", "dien thoai pin trau", "iphone cu", "samsung cu",

  // Laptop & Máy tính
  "laptop", "macbook", "laptop gaming", "dell", "hp", "lenovo", "asus", "acer", "msi", "lg gram",
  "macbook air", "macbook pro", "laptop sinh vien", "laptop van phong", "laptop do hoa",
  "laptop core i5", "laptop core i7", "laptop ryzen", "laptop 16 inch", "laptop 14 inch",
  "may tinh ban", "pc gaming", "may tinh bo", "ban phim co", "chuot gaming",
  "man hinh 4k", "man hinh cong", "man hinh 27 inch", "man hinh gaming", "man hinh 144hz",

  // Tai nghe & Loa
  "tai nghe", "tai nghe bluetooth", "tai nghe chong on", "airpods", "sony wh1000xm5",
  "tai nghe gaming", "tai nghe true wireless", "earbuds", "tai nghe co day", "tai nghe in ear",
  "loa bluetooth", "loa karaoke", "loa vi tinh", "soundbar", "loa jbl",
  "loa marshall", "tai nghe samsung", "tai nghe xiaomi", "tai nghe apple", "tai nghe bose",

  // Phụ kiện
  "sac du phong", "pin du phong", "sac nhanh", "cap sac", "sac khong day",
  "op lung", "cuong luc", "bao da", "op iphone", "op samsung",
  "chuot bluetooth", "ban phim bluetooth", "hub usb", "usb c", "the nho",
  "the sd", "usb", "o cung ngoai", "ssd ngoai", "webcam",
  "giay do man hinh", "gia do man hinh", "gia dung laptop", "vo lung may tinh", "tan nhiet laptop",

  // Linh kiện PC
  "ram", "ram ddr5", "ram laptop", "cpu", "intel core i9",
  "amd ryzen", "bo xu ly", "card do hoa", "rtx 4090", "rtx 4080",
  "vga gaming", "ssd", "ssd nvme", "o cung ssd", "hdd",
  "mainboard", "cpu cooler", "tan nhiet", "psu", "nguon may tinh",
  "case may tinh", "vo case", "case gaming", "rgb", "fan case",

  // Máy ảnh & Quay phim
  "may anh", "may anh mirrorless", "may anh sony", "may anh canon", "may anh fuji",
  "ong kinh", "lens", "tripod", "chan may", "gimbal",
  "action camera", "gopro", "may quay phim", "may quay vlog", "camera hanh dong",
  "may anh gia re", "may anh cu", "may anh cho nguoi moi", "micro thu am", "den ring light",

  // Đồng hồ & Wearable
  "dong ho thong minh", "smartwatch", "apple watch", "samsung watch", "garmin",
  "xiaomi band", "vong tay thong minh", "dong ho the thao", "dong ho chay bo",
  "dong ho gps", "tai nghe khong day", "the duc thong minh", "vong tay theo doi suc khoe",
  "may do huyet ap", "may do nhip tim",

  // Thiết bị nhà thông minh
  "thiet bi nha thong minh", "smart home", "camera an ninh", "camera ip", "camera wifi",
  "den thong minh", "o cam thong minh", "router wifi", "mesh wifi", "wifi extender",
  "tv thong minh", "smart tv", "samsung tv", "lg tv", "sony tv",
  "loa thong minh", "may loc khong khi", "robot hut bui", "may loc nuoc", "noi com dien tu",

  // Gaming
  "tai nghe gaming", "chuot gaming", "ban phim gaming", "ghe gaming", "ban gaming",
  "man hinh gaming", "controller", "tay cam", "gamepad", "joystick",
  "headset gaming", "microphone gaming", "webcam gaming", "capture card", "ps5",
  "xbox", "may choi game", "nintendo switch", "gaming gear", "rgb gaming",

  // Máy in & Văn phòng
  "may in", "may in laser", "may in mau", "may in phun", "muc in",
  "may scan", "may photocopy", "may chieu", "may tinh bang", "tablet",
  "ipad", "android tablet", "samsung tab", "may tinh xach tay", "laptop van phong",
  "but stylus", "apple pencil", "ban di chuot", "lo xo", "may tinh casio",
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