// Same-algorithm JavaScript twins of the Copeland ports (not the original TS benches).
const quick = process.argv[2] === 'quick'
const only = process.argv[3] ?? ''

function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2) }
function runFib(bias) { let s = 0; for (let i = 0; i < 5; i++) s = s + fib(32 + i % 2 + bias); return s }

function runNBody(steps) {
  const pi = 3.141592653589793, solarMass = 4.0 * pi * pi, dpy = 365.24
  const x = new Float64Array(5), y = new Float64Array(5), z = new Float64Array(5)
  const vx = new Float64Array(5), vy = new Float64Array(5), vz = new Float64Array(5), mass = new Float64Array(5)
  const set = (i, a, b, c, d, e, f, m) => { x[i] = a; y[i] = b; z[i] = c; vx[i] = d * dpy; vy[i] = e * dpy; vz[i] = f * dpy; mass[i] = m }
  set(0, 0, 0, 0, 0, 0, 0, solarMass)
  set(1, 4.84143144246472090e+00, -1.16032004402742839e+00, -1.03622044471123109e-01, 1.66007664274403694e-03, 7.69901118419740425e-03, -6.90460016972063023e-05, 9.54791938424326609e-04 * solarMass)
  set(2, 8.34336671824457987e+00, 4.12479856412430479e+00, -4.03523417114321381e-01, -2.76742510726862411e-03, 4.99852801234917238e-03, 2.30417297573763929e-05, 2.85885980666130812e-04 * solarMass)
  set(3, 1.28943695621391310e+01, -1.51111514016986312e+01, -2.23307578892655734e-01, 2.96460137564761618e-03, 2.37847173959480950e-03, -2.96589568540237556e-05, 4.36624404335156298e-05 * solarMass)
  set(4, 1.53796971148509165e+01, -2.59193146099879641e+01, 1.79258772950371181e-01, 2.68067772490389322e-03, 1.62824170038242295e-03, -9.51592254519715870e-05, 5.15138902046611451e-05 * solarMass)
  let px = 0, py = 0, pz = 0
  for (let i = 0; i < 5; i++) { px += vx[i] * mass[i]; py += vy[i] * mass[i]; pz += vz[i] * mass[i] }
  vx[0] = -px / solarMass; vy[0] = -py / solarMass; vz[0] = -pz / solarMass
  const dt = 0.01, n = 5
  for (let s = 0; s < steps; s++) {
    for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
      const dx = x[i] - x[j], dy = y[i] - y[j], dz = z[i] - z[j]
      const d2 = dx * dx + dy * dy + dz * dz, mag = dt / (d2 * Math.sqrt(d2)), mi = mass[i], mj = mass[j]
      vx[i] -= dx * mj * mag; vy[i] -= dy * mj * mag; vz[i] -= dz * mj * mag
      vx[j] += dx * mi * mag; vy[j] += dy * mi * mag; vz[j] += dz * mi * mag
    }
    for (let i = 0; i < n; i++) { x[i] += dt * vx[i]; y[i] += dt * vy[i]; z[i] += dt * vz[i] }
  }
  let e = 0
  for (let i = 0; i < n; i++) {
    e += 0.5 * mass[i] * (vx[i] * vx[i] + vy[i] * vy[i] + vz[i] * vz[i])
    for (let j = i + 1; j < n; j++) { const dx = x[i] - x[j], dy = y[i] - y[j], dz = z[i] - z[j]; e -= (mass[i] * mass[j]) / Math.sqrt(dx * dx + dy * dy + dz * dz) }
  }
  return e
}

const LEAF = { tag: 0 }
function make(d) { return d === 0 ? { tag: 1, left: LEAF, right: LEAF } : { tag: 1, left: make(d - 1), right: make(d - 1) } }
function check(t) { return t.tag === 0 ? 0 : 1 + check(t.left) + check(t.right) }
function runTrees(maxD) {
  const longLived = make(maxD); let total = 0
  for (let d = 4; d <= maxD; d += 2) { const it = 2 ** (maxD - d + 4); let c = 0; for (let i = 0; i < it; i++) c += check(make(d)); total += c }
  return total + check(longLived)
}

function runSieve(limit) {
  let count = 0
  for (let rep = 0; rep < 5; rep++) {
    const composite = new Uint8Array(limit + 1); count = 0
    for (let i = 2; i <= limit; i++) if (!composite[i]) { count++; if (i <= Math.trunc(limit / i)) for (let j = i * i; j <= limit; j += i) composite[j] = 1 }
  }
  return count
}

function quickSort(v, low, high) {
  let lo = low, hi = high
  while (lo < hi) {
    const pivot = v[lo + ((hi - lo) >> 1)]; let i = lo, j = hi
    while (i <= j) { while (v[i] < pivot) i++; while (v[j] > pivot) j--; if (i <= j) { const t = v[i]; v[i] = v[j]; v[j] = t; i++; j-- } }
    if (j - lo < hi - i) { quickSort(v, lo, j); lo = i } else { quickSort(v, i, hi); hi = j }
  }
}
function runArrays(rounds, count) {
  let seed = 42, acc = 0
  for (let r = 0; r < rounds; r++) {
    const xs = new Int32Array(count)
    for (let i = 0; i < count; i++) { seed = (seed * 1103515245 + 12345) % 2147483648; xs[i] = Math.trunc(seed % 100000) }
    quickSort(xs, 0, count - 1)
    let kept = 0; for (let i = 0; i < count; i++) if ((xs[i] * 2) % 3 === 0) kept++
    const ys = new Int32Array(kept); let k = 0
    for (let i = 0; i < count; i++) { const d = xs[i] * 2; if (d % 3 === 0) ys[k++] = d }
    let sum = 0; for (const y of ys) sum += y
    acc += sum + xs[Math.trunc(count / 2)]
  }
  return acc
}

function runStrings(rounds, count) {
  let total = 0
  for (let r = 0; r < rounds; r++) {
    const parts = []; for (let i = 0; i < count; i++) parts.push('item' + i)
    const joined = parts.join(','), back = joined.split(',')
    let hash = 0; for (let i = 0; i < joined.length; i++) hash = (Math.imul(hash, 31) + joined.charCodeAt(i)) | 0
    total = (total + back.length + hash % 256 + joined.indexOf('item99999')) | 0
  }
  return total
}

function runObjects(rounds, count) {
  const ids = Array.from({ length: count }, (_, i) => i); let sum = 0
  for (let r = 0; r < rounds; r++) {
    const points = ids.map((id) => ({ x: id, y: id * 0.5, tag: id % 2 === 0 ? 'a' : 'b' }))
    for (const p of points) if (p.tag === 'a') sum += p.x + p.y
  }
  return sum
}

function makeAdder(k) { return (v) => v + k }
function runClosures(n) {
  const adders = []; for (let i = 0; i < 16; i++) adders.push(makeAdder(i))
  let acc = 0; for (let i = 0; i < n; i++) acc = adders[i % 16](acc) % 1000003
  return acc
}

function run(name, body) {
  if (only !== '' && only !== name) return;
  const trials = [];
  for (let attempt = 0; attempt < 3; attempt++) {
    const heapBefore = process.memoryUsage().heapUsed;
    const start = performance.now();
    const result = body();
    trials.push({
      result: String(result),
      milliseconds: performance.now() - start,
      heapUsedDeltaBytes: process.memoryUsage().heapUsed - heapBefore
    });
  }
  console.log(JSON.stringify({ name, quick, trials, peakWorkingSetBytes: process.resourceUsage().maxRSS * 1024 }));
}
run('fib', () => runFib(0))
run('nbody', () => runNBody(quick ? 1000 : 10_000_000).toFixed(9))
run('trees', () => runTrees(quick ? 8 : 18))
run('sieve', () => runSieve(quick ? 1000 : 20_000_000))
run('arrays', () => runArrays(quick ? 1 : 10, quick ? 1000 : 1_000_000))
run('strings', () => runStrings(quick ? 1 : 20, quick ? 1000 : 100_000))
run('objects', () => runObjects(quick ? 1 : 50, quick ? 1000 : 200_000))
run('closures', () => runClosures(quick ? 1000 : 100_000_000))

function runNativeStrings(rounds, count) {
  let total = 0;
  for (let round = 0; round < rounds; round++) {
    const parts = new Array(count).fill('');
    for (let index = 0; index < count; index++) {
      parts[index] = `item${index}`;
    }
    const joined = parts.slice().join(',');
    const back = joined.split(',');
    let hash = 0;
    for (let index = 0; index < joined.length; index++) {
      hash = (Math.imul(hash, 31) + joined.charCodeAt(index)) | 0;
    }
    total = (total + back.length + hash % 256 + joined.indexOf('item99999')) | 0;
  }
  return total;
}

function runMaps(rounds, count) {
  let checksum = 0;
  for (let round = 0; round < rounds; round++) {
    const values = new Map();
    for (let index = 0; index < count; index++) {
      values.set(`key${index}`, index);
    }
    for (let index = 0; index < count; index++) {
      const key = `key${index}`;
      values.set(key, values.get(key) + 1);
      checksum = (checksum + values.get(key)) | 0;
    }
    for (let index = 0; index < count; index++) {
      if (index % 2 === 0) values.delete(`key${index}`);
    }
    checksum = (checksum + values.size) | 0;
  }
  return checksum;
}
run('native-strings', () => runNativeStrings(quick ? 1 : 20, quick ? 1000 : 100_000));
run('maps', () => runMaps(quick ? 1 : 10, quick ? 1000 : 100_000));
