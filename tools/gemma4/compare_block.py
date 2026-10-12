"""Offline NumPy oracle for the pinned Gemma graph and local Q4 artifact.

Never imported or invoked by inference. Reads selected checkpoint matrices and
compares a multi-token prefill against native and incremental cache execution.
Algorithm reference: Hugging Face Gemma4 Unified, Apache-2.0; see NOTICE.md.
"""
import argparse
import json
import pathlib
import struct
import numpy as np


def bf16(value):
    value = np.asarray(value, dtype=np.float32)
    bits = value.view(np.uint32)
    rounded = bits + np.uint32(32767) + ((bits >> np.uint32(16)) & np.uint32(1))
    return (rounded & np.uint32(0xffff0000)).view(np.float32)


def run(model, output):
    source = model / 'model.safetensors'
    with source.open('rb') as stream:
        size = struct.unpack('<Q', stream.read(8))[0]
        header = json.loads(stream.read(size))
    data = np.memmap(source, dtype=np.uint8, mode='r', offset=8 + size)
    manifest = json.loads((model / 'q4b32-v1/q4.json').read_text())
    matrices = {item['Name']: item for item in manifest['Matrices']}
    packed = np.memmap(model / 'q4b32-v1/q4.bin', dtype='<u4', mode='r')

    def weight(name):
        item = header[name]
        first, end = item['data_offsets']
        return (data[first:end].view('<u2').astype(np.uint32) << 16).view(np.float32).reshape(item['shape'])

    def linear(value, name):
        item = matrices[name]
        words = packed[item['Offset'] // 4:(item['Offset'] + item['ByteLength']) // 4].reshape(-1, 5)
        scales = words[:, :1].view(np.float32)
        nibbles = ((words[:, 1:, None] >> (np.arange(8, dtype=np.uint32) * 4)) & 15).reshape(-1, 32)
        decoded = ((nibbles.astype(np.float32) - 8) * scales).reshape(item['Rows'], item['Columns'])
        return bf16(value @ decoded.T)

    def norm(value, gamma):
        return bf16(value * np.power(np.mean(value * value, axis=-1, keepdims=True) + 1e-6, -.5) * gamma)

    def rope(value, positions, proportion, theta):
        width = value.shape[-1]
        frequency = np.zeros(width // 2, dtype=np.float32)
        count = int(width / 2 * proportion)
        frequency[:count] = np.power(theta, -np.arange(count, dtype=np.float32) * 2 / width)
        angles = positions.astype(np.float32)[:, None, None] * frequency
        cosine, sine = bf16(np.cos(angles)), bf16(np.sin(angles))
        first, second = np.split(value, 2, axis=-1)
        return np.concatenate((bf16(bf16(first * cosine) - bf16(second * sine)),
                               bf16(bf16(second * cosine) + bf16(first * sine))), axis=-1)

    prefix = 'model.language_model.'
    hidden = bf16(weight(prefix + 'embed_tokens.weight')[[2, 2532]] * bf16(np.sqrt(np.float32(3840))))
    expected = hidden
    for layer in range(6):
        name = prefix + f'layers.{layer}.'
        global_layer = layer % 6 == 5
        width = 512 if global_layer else 256
        kv_heads = 1 if global_layer else 8
        x = norm(expected, weight(name + 'input_layernorm.weight'))
        query = linear(x, name + 'self_attn.q_proj.weight').reshape(2, 16, width)
        key = linear(x, name + 'self_attn.k_proj.weight').reshape(2, kv_heads, width)
        value = key.copy() if global_layer else linear(x, name + 'self_attn.v_proj.weight').reshape(2, kv_heads, width)
        proportion, theta = (.25, 1000000) if global_layer else (1, 10000)
        query = rope(norm(query, weight(name + 'self_attn.q_norm.weight')), np.arange(2), proportion, theta)
        key = rope(norm(key, weight(name + 'self_attn.k_norm.weight')), np.arange(2), proportion, theta)
        value = norm(value, 1)
        key = np.repeat(key, 16 // kv_heads, axis=1)
        value = np.repeat(value, 16 // kv_heads, axis=1)
        scores = bf16(np.einsum('qhd,khd->qhk', query, key))
        scores[0, :, 1] = -np.finfo(np.float32).max
        probabilities = np.exp(scores - scores.max(axis=-1, keepdims=True))
        probabilities = bf16(probabilities / probabilities.sum(axis=-1, keepdims=True))
        attended = bf16(np.einsum('qhk,khd->qhd', probabilities, value)).reshape(2, -1)
        branch = norm(linear(attended, name + 'self_attn.o_proj.weight'), weight(name + 'post_attention_layernorm.weight'))
        expected = bf16(expected + branch)
        x = norm(expected, weight(name + 'pre_feedforward_layernorm.weight'))
        gate = linear(x, name + 'mlp.gate_proj.weight')
        up = linear(x, name + 'mlp.up_proj.weight')
        with np.errstate(over='ignore'):
            gelu = gate / (1 + np.exp(-2 * .7978845608 * (gate + .044715 * gate**3)))
        gated = bf16(bf16(gelu) * up)
        branch = norm(linear(gated, name + 'mlp.down_proj.weight'), weight(name + 'post_feedforward_layernorm.weight'))
        expected = bf16(bf16(expected + branch) * weight(name + 'layer_scalar'))
    actual = np.fromfile(output / 'block-native.f32', dtype='<f4').reshape(expected.shape)
    incremental = np.fromfile(output / 'block-incremental.f32', dtype='<f4').reshape(expected.shape)

    def metrics(a, b):
        error = (a.astype(np.float64) - b)
        return dict(MaximumAbsoluteError=float(np.abs(error).max()),
                    RelativeRms=float(np.linalg.norm(error) / max(np.linalg.norm(b), 1e-12)))
    reference = metrics(actual, expected)
    cache = metrics(incremental, actual)
    evidence = dict(Accepted=reference['RelativeRms'] < .015 and cache['RelativeRms'] < .001,
                    Oracle='independent NumPy Q4B32 Gemma blocks', Layers=6,
                    Reference=reference, IncrementalCache=cache, FullModelParity=False)
    (output / 'block-comparison.json').write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2))
    if not evidence['Accepted']:
        raise SystemExit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', type=pathlib.Path, default=pathlib.Path('artifacts/local/models/gemma4-12b'))
    parser.add_argument('--output', type=pathlib.Path, default=pathlib.Path('artifacts/local/gemma4'))
    options = parser.parse_args()
    run(options.model, options.output)
