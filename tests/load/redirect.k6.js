// Optional k6 equivalent of tests/UrlShortener.LoadTests (for teams that standardise on k6).
//   k6 run -e BASE_URL=http://localhost:5104 tests/load/redirect.k6.js
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5104';

export const options = {
  vus: 64,
  duration: '20s',
  thresholds: {
    http_req_failed: ['rate<0.001'],
    'http_req_duration{name:redirect}': ['p(95)<25'],
  },
};

export function setup() {
  const codes = [];
  for (let i = 0; i < 20; i++) {
    const res = http.post(`${BASE}/api/v1/links`, JSON.stringify({ url: `https://example.com/k6/${i}` }),
      { headers: { 'Content-Type': 'application/json' } });
    codes.push(res.json('code'));
  }
  return { codes };
}

export default function (data) {
  const code = data.codes[Math.floor(Math.random() * data.codes.length)];
  const res = http.get(`${BASE}/${code}`, { redirects: 0, tags: { name: 'redirect' } });
  check(res, { 'is 302': (r) => r.status === 302 });
}
