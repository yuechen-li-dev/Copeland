using System;

record NBodyState {
    x: MutableArray<float>;
    y: MutableArray<float>;
    z: MutableArray<float>;
    vx: MutableArray<float>;
    vy: MutableArray<float>;
    vz: MutableArray<float>;
    mass: MutableArray<float>;
}

function setBody(state: NBodyState, index: int, x: float, y: float, z: float, vx: float, vy: float, vz: float, mass: float): void {
    const daysPerYear: float = 365.24;
    state.x[index] = x;
    state.y[index] = y;
    state.z[index] = z;
    state.vx[index] = vx * daysPerYear;
    state.vy[index] = vy * daysPerYear;
    state.vz[index] = vz * daysPerYear;
    state.mass[index] = mass;
}

function createSystem(solarMass: float): NBodyState {
    const state: NBodyState = {
        x: MutableArray<float>(5),
        y: MutableArray<float>(5),
        z: MutableArray<float>(5),
        vx: MutableArray<float>(5),
        vy: MutableArray<float>(5),
        vz: MutableArray<float>(5),
        mass: MutableArray<float>(5)
    };
    setBody(state, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, solarMass);
    setBody(state, 1, 4.84143144246472090e+00, -1.16032004402742839e+00, -1.03622044471123109e-01, 1.66007664274403694e-03, 7.69901118419740425e-03, -6.90460016972063023e-05, 9.54791938424326609e-04 * solarMass);
    setBody(state, 2, 8.34336671824457987e+00, 4.12479856412430479e+00, -4.03523417114321381e-01, -2.76742510726862411e-03, 4.99852801234917238e-03, 2.30417297573763929e-05, 2.85885980666130812e-04 * solarMass);
    setBody(state, 3, 1.28943695621391310e+01, -1.51111514016986312e+01, -2.23307578892655734e-01, 2.96460137564761618e-03, 2.37847173959480950e-03, -2.96589568540237556e-05, 4.36624404335156298e-05 * solarMass);
    setBody(state, 4, 1.53796971148509165e+01, -2.59193146099879641e+01, 1.79258772950371181e-01, 2.68067772490389322e-03, 1.62824170038242295e-03, -9.51592254519715870e-05, 5.15138902046611451e-05 * solarMass);
    return state;
}

function advance(s: NBodyState, dt: float): void {
    const n: int = s.x.length;
    for (let i: int = 0; i < n; i = i + 1) {
        for (let j: int = i + 1; j < n; j = j + 1) {
            const dx: float = s.x[i] - s.x[j];
            const dy: float = s.y[i] - s.y[j];
            const dz: float = s.z[i] - s.z[j];
            const d2: float = dx * dx + dy * dy + dz * dz;
            const mag: float = dt / (d2 * Math.Sqrt(d2));
            const mi: float = s.mass[i];
            const mj: float = s.mass[j];
            s.vx[i] = s.vx[i] - dx * mj * mag;
            s.vy[i] = s.vy[i] - dy * mj * mag;
            s.vz[i] = s.vz[i] - dz * mj * mag;
            s.vx[j] = s.vx[j] + dx * mi * mag;
            s.vy[j] = s.vy[j] + dy * mi * mag;
            s.vz[j] = s.vz[j] + dz * mi * mag;
        }
    }
    for (let i: int = 0; i < n; i = i + 1) {
        s.x[i] = s.x[i] + dt * s.vx[i];
        s.y[i] = s.y[i] + dt * s.vy[i];
        s.z[i] = s.z[i] + dt * s.vz[i];
    }
}

function energy(s: NBodyState): float {
    let e: float = 0.0;
    const n: int = s.x.length;
    for (let i: int = 0; i < n; i = i + 1) {
        e = e + 0.5 * s.mass[i] * (s.vx[i] * s.vx[i] + s.vy[i] * s.vy[i] + s.vz[i] * s.vz[i]);
        for (let j: int = i + 1; j < n; j = j + 1) {
            const dx: float = s.x[i] - s.x[j];
            const dy: float = s.y[i] - s.y[j];
            const dz: float = s.z[i] - s.z[j];
            e = e - (s.mass[i] * s.mass[j]) / Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
    return e;
}

export function runNBody(steps: int): float {
    const pi: float = 3.141592653589793;
    const solarMass: float = 4.0 * pi * pi;
    const s: NBodyState = createSystem(solarMass);
    let px: float = 0.0;
    let py: float = 0.0;
    let pz: float = 0.0;
    for (let i: int = 0; i < 5; i = i + 1) {
        px = px + s.vx[i] * s.mass[i];
        py = py + s.vy[i] * s.mass[i];
        pz = pz + s.vz[i] * s.mass[i];
    }
    s.vx[0] = -px / solarMass;
    s.vy[0] = -py / solarMass;
    s.vz[0] = -pz / solarMass;
    for (let step: int = 0; step < steps; step = step + 1) {
        advance(s, 0.01);
    }
    return energy(s);
}
