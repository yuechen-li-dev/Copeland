"""Sol-authored controls. No claim of matching expert kernels or equal search budgets."""

import triton
import triton.language as tl


@triton.jit
def bias_silu(X, Bias, Y, COUNT: tl.constexpr, WIDTH: tl.constexpr, BLOCK: tl.constexpr):
    index = tl.program_id(0) * BLOCK + tl.arange(0, BLOCK)
    value = tl.load(X + index, index < COUNT, other=0)
    bias = tl.load(Bias + index % WIDTH, index < COUNT, other=0)
    shifted = value + bias
    result = shifted / (1 + tl.exp(-shifted))
    tl.store(Y + index, result, index < COUNT)


@triton.jit
def row_operation(X, Gamma, Y, WIDTH: tl.constexpr, RMS: tl.constexpr, BLOCK: tl.constexpr):
    row = tl.program_id(0)
    column = tl.arange(0, BLOCK)
    value = tl.load(X + row * WIDTH + column, column < WIDTH, other=0)
    if RMS:
        squares = tl.sum(value * value, 0) / WIDTH
        gamma = tl.load(Gamma + column, column < WIDTH, other=0)
        result = value * tl.rsqrt(squares + 1e-5) * gamma
    else:
        masked = tl.where(column < WIDTH, value, -float("inf"))
        exponential = tl.exp(masked - tl.max(masked, 0))
        result = exponential / tl.sum(exponential, 0)
    tl.store(Y + row * WIDTH + column, result, column < WIDTH)


@triton.jit
def matmul(A, B, Bias, C, M: tl.constexpr, N: tl.constexpr, K: tl.constexpr,
           EPILOGUE: tl.constexpr, BM: tl.constexpr, BN: tl.constexpr, BK: tl.constexpr):
    # A conventional blocked matmul. Strict IEEE dot avoids TF32 advantages.
    rows = tl.program_id(0) * BM + tl.arange(0, BM)
    columns = tl.program_id(1) * BN + tl.arange(0, BN)
    inner = tl.arange(0, BK)
    accumulator = tl.full((BM, BN), 0, tl.float32)
    for offset in range(tl.cdiv(K, BK)):
        k = offset * BK + inner
        a = tl.load(A + rows[:, None] * K + k[None, :],
                    (rows[:, None] < M) & (k[None, :] < K), other=0)
        b = tl.load(B + k[:, None] * N + columns[None, :],
                    (k[:, None] < K) & (columns[None, :] < N), other=0)
        accumulator = tl.dot(a, b, accumulator, input_precision="ieee")
    if EPILOGUE:
        bias = tl.load(Bias + columns, columns < N, other=0)
        shifted = accumulator + bias[None, :]
        accumulator = shifted / (1 + tl.exp(-shifted))
    tl.store(C + rows[:, None] * N + columns[None, :], accumulator,
             (rows[:, None] < M) & (columns[None, :] < N))
