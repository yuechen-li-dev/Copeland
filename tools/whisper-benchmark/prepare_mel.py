"""Prepare the same 30-second Tiny.en log-mel window for both runtimes."""

import argparse
from pathlib import Path
import struct
import wave

import numpy as np


def prepare(checkpoint, audio, output):
    sample_rate = 16000
    frame_size = 400
    hop = 160
    frame_count = 3000
    with checkpoint.open("rb") as model:
        header = struct.unpack("<14I", model.read(56))
        if header != (0x67676D6C, 51864, 1500, 384, 6, 4, 448, 384, 6, 4, 80, 1, 80, 201):
            raise ValueError("Expected the ggml Tiny.en F16 checkpoint")
        filters = np.frombuffer(model.read(80 * 201 * 4), dtype="<f4").reshape(80, 201)

    with wave.open(str(audio), "rb") as source:
        if (source.getframerate(), source.getnchannels(), source.getsampwidth()) != (sample_rate, 1, 2):
            raise ValueError("Expected mono PCM16 audio at 16 kHz")
        sample_count = source.getnframes()
        if not frame_size < sample_count <= sample_rate * 30 - frame_size // 2:
            raise ValueError("Expected a short clip whose final centered frame fits the encoder window")
        pcm = np.frombuffer(source.readframes(sample_count), dtype="<i2").astype(np.float32) / 32768

    center = frame_size // 2
    padded = np.zeros(frame_count * hop + frame_size, dtype=np.float32)
    padded[:center] = pcm[1:center + 1][::-1]
    padded[center:center + sample_count] = pcm
    frames = np.lib.stride_tricks.sliding_window_view(padded, frame_size)[::hop][:frame_count]
    hann = (0.5 - 0.5 * np.cos(2 * np.pi * np.arange(frame_size) / frame_size)).astype(np.float32)
    fft = np.fft.rfft(frames * hann)
    power = np.square(fft.real) + np.square(fft.imag)
    log_mel = np.log10(np.maximum(power @ filters.T, 1e-10))
    mel = (np.maximum(log_mel, log_mel.max() - 8.0) + 4.0) / 4.0
    mel.astype("<f4").tofile(output)
    return sample_count / sample_rate


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("audio", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    print(prepare(arguments.checkpoint, arguments.audio, arguments.output))
