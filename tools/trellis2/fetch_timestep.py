"""Fetch pinned TRELLIS.2 timestep/shared conditioning networks, without executing upstream code.

Uses Python's standard library for HTTP/safetensors extraction and the existing
NumPy installation for an independent FP64 reference. No CUDA packages needed.
"""

import argparse
import hashlib
import json
import pathlib
import struct
import urllib.request

import numpy as np


MODEL_REVISION = "af44b45f2e35a493886929c6d786e563ec68364d"
SOURCE_REVISION = "75fbf0183001ed9876c8dbb35de6b68552ee08bd"
MODEL_ROOT = f"https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/{MODEL_REVISION}"
CHECKPOINT = "ckpts/ss_flow_img_dit_1_3B_64_bf16.safetensors"
MAX_RANGE_BYTES = 16 * 1024 * 1024
NAMES = [
    "t_embedder.mlp.0.weight",
    "t_embedder.mlp.0.bias",
    "t_embedder.mlp.2.weight",
    "t_embedder.mlp.2.bias",
    "adaLN_modulation.1.weight",
    "adaLN_modulation.1.bias",
]


def fetch_range(start, count):
    if count <= 0 or count > MAX_RANGE_BYTES:
        raise ValueError("Requested checkpoint range exceeds the bounded fetch budget")
    end = start + count - 1
    # Distinct URLs avoid intermediaries serving a cached range for another request.
    url = f"{MODEL_ROOT}/{CHECKPOINT}?download=true&range={start}-{end}"
    request = urllib.request.Request(url, headers={"Range": f"bytes={start}-{end}"})
    with urllib.request.urlopen(request, timeout=90) as response:
        content_range = response.headers.get("Content-Range", "")
        if response.status != 206 or content_range != f"bytes {start}-{end}/2584426920":
            raise RuntimeError(f"Server did not honor the exact pinned range: {response.status}, {content_range}")
        data = response.read(count + 1)
        if len(data) != count:
            raise RuntimeError("Truncated or oversized checkpoint range")
        return data


def fetch_text(url, target):
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read(1024 * 1024 + 1)
    if len(data) > 1024 * 1024:
        raise RuntimeError("Unexpectedly large source/config response")
    target.write_bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = pathlib.Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    fetch_text(f"{MODEL_ROOT}/ckpts/ss_flow_img_dit_1_3B_64_bf16.json", output / "model.json")
    fetch_text(f"{MODEL_ROOT}/pipeline.json", output / "pipeline.json")
    source_root = f"https://raw.githubusercontent.com/microsoft/TRELLIS.2/{SOURCE_REVISION}"
    for path, name in [
        ("trellis2/models/sparse_structure_flow.py", "sparse_structure_flow.py"),
        ("trellis2/pipelines/trellis2_image_to_3d.py", "trellis2_image_to_3d.py"),
        ("LICENSE", "TRELLIS-LICENSE.txt"),
    ]:
        fetch_text(f"{source_root}/{path}", output / name)

    header_size = struct.unpack("<Q", fetch_range(0, 8))[0]
    if header_size > 4 * 1024 * 1024:
        raise RuntimeError("Checkpoint header exceeds 4 MiB")
    header_bytes = fetch_range(8, header_size)
    (output / "checkpoint-header.json").write_bytes(header_bytes)
    header = json.loads(header_bytes)
    blobs = []
    selected = {}
    provenance = []
    arrays = {}
    offset = 0
    for name in NAMES:
        tensor = header[name]
        if tensor["dtype"] not in ("F32", "BF16", "F16"):
            raise RuntimeError(f"Unsupported floating-point weight encoding: {name}, {tensor}")
        start, end = tensor["data_offsets"]
        parts = []
        for chunk_start in range(start, end, MAX_RANGE_BYTES):
            parts.append(fetch_range(8 + header_size + chunk_start, min(MAX_RANGE_BYTES, end - chunk_start)))
        blob = b"".join(parts)
        selected[name] = {"dtype": tensor["dtype"], "shape": tensor["shape"], "data_offsets": [offset, offset + len(blob)]}
        offset += len(blob)
        blobs.append(blob)
        if tensor["dtype"] == "BF16":
            words = np.frombuffer(blob, dtype="<u2").astype(np.uint32) << 16
            values = words.view(np.float32)
        elif tensor["dtype"] == "F16":
            values = np.frombuffer(blob, dtype="<f2").astype(np.float32)
        else:
            values = np.frombuffer(blob, dtype="<f4")
        arrays[name] = values.reshape(tensor["shape"])
        provenance.append({"name": name, "shape": tensor["shape"], "checkpointDataOffsets": [start, end],
                           "sha256": hashlib.sha256(blob).hexdigest(), "bytes": len(blob)})
        print(f"Fetched {name}: {tensor['shape']}, {len(blob):,} bytes", flush=True)

    selected["__metadata__"] = {"source": "microsoft/TRELLIS.2-4B", "revision": MODEL_REVISION,
                                "scope": "Complete timestep MLP and shared AdaLN modulation, before BF16 cast"}
    subset_header = json.dumps(selected, separators=(",", ":")).encode("utf-8")
    subset_header += b" " * (-len(subset_header) % 8)
    subset = struct.pack("<Q", len(subset_header)) + subset_header + b"".join(blobs)
    (output / "timestep.safetensors").write_bytes(subset)

    # Actual network input t is a scalar timestep, not an image/latent. Deliberately
    # cover the 0..1000 diffusion domain plus non-integer values.
    times = np.array([0, 0.001, 1, 12.5, 50, 125, 250, 333.3, 500, 625, 750, 875, 950, 999, 999.99, 1000], dtype=np.float32)
    frequencies = np.exp(-np.log(10000.0) * np.arange(128, dtype=np.float32) / 128).astype(np.float32)
    angles = times[:, None] * frequencies[None, :]
    embedding = np.concatenate([np.cos(angles), np.sin(angles)], axis=1).astype(np.float32)
    first = embedding.astype(np.float64) @ arrays[NAMES[0]].astype(np.float64).T + arrays[NAMES[1]]
    hidden = first / (1.0 + np.exp(-first))
    expected = hidden @ arrays[NAMES[2]].astype(np.float64).T + arrays[NAMES[3]]
    modulation_input = expected / (1.0 + np.exp(-expected))
    modulation = modulation_input @ arrays[NAMES[4]].astype(np.float64).T + arrays[NAMES[5]]
    (output / "input.f32").write_bytes(embedding.astype("<f4").tobytes())
    (output / "expected.f32").write_bytes(expected.astype("<f4").tobytes())
    (output / "modulation-expected.f32").write_bytes(modulation.astype("<f4").tobytes())
    evidence = {"modelRevision": MODEL_REVISION, "sourceRevision": SOURCE_REVISION,
                "checkpoint": f"{MODEL_ROOT}/{CHECKPOINT}", "checkpointBytes": 2584426920,
                "headerSha256": hashlib.sha256(header_bytes).hexdigest(),
                "subsetSha256": hashlib.sha256(subset).hexdigest(), "downloadedWeightBytes": offset,
                "tensors": provenance, "timesteps": times.tolist(), "inputShape": list(embedding.shape),
                "outputShape": list(expected.shape), "oracle": "NumPy FP64 affine/SiLU/affine; upstream FP32 frequency features",
                "inputSha256": hashlib.sha256((output / "input.f32").read_bytes()).hexdigest(),
                "expectedSha256": hashlib.sha256((output / "expected.f32").read_bytes()).hexdigest(),
                "modulationExpectedSha256": hashlib.sha256((output / "modulation-expected.f32").read_bytes()).hexdigest(),
                "modulationShape": list(modulation.shape),
                "scope": "No denoising, image conditioning, sparse decoding or generated asset"}
    (output / "provenance.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print(json.dumps({"weightBytes": offset, "outputShape": list(expected.shape),
                      "modulationShape": list(modulation.shape), "outputMaximum": float(np.max(np.abs(modulation)))}))


if __name__ == "__main__":
    main()
