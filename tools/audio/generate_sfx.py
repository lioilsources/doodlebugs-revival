#!/usr/bin/env python3
"""Procedural 8-bit one-shots for flight feel, arsenal and hangar
(Prompts/28-30), stdlib only - same tradition as the rest of Resources/Sfx.

    python3 tools/audio/generate_sfx.py            # write every clip + .meta
    python3 tools/audio/generate_sfx.py whoosh     # just one

Mono 44.1 kHz 16-bit, written to Assets/Doodlebugs/Resources/Sfx/sfx_<key>.wav.
A .meta is written only when missing (stable GUIDs across re-runs).
"""
import math
import random
import sys
import uuid
import wave
from pathlib import Path

SR = 44100
OUT = Path(__file__).parents[2] / "Assets/Doodlebugs/Resources/Sfx"

META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def square(phase, duty=0.5):
    return 1.0 if phase % 1.0 < duty else -1.0


def tri(phase):
    p = phase % 1.0
    return 4 * p - 1 if p < 0.5 else 3 - 4 * p


class Noise:
    """Lo-fi noise: a new random value every `hold` samples."""

    def __init__(self, hold=4, seed=1):
        self.rng = random.Random(seed)
        self.hold = hold
        self.i = 0
        self.v = 0.0

    def __call__(self):
        if self.i % self.hold == 0:
            self.v = self.rng.uniform(-1, 1)
        self.i += 1
        return self.v


def env(t, length, attack=0.005, release=None):
    """Linear attack, linear release to zero at `length`."""
    release = length - attack if release is None else release
    if t < attack:
        return t / attack
    if t > length - release:
        return max(0.0, (length - t) / release)
    return 1.0


def sweep(length, f0, f1, wave_fn, vol=0.6, curve=1.0):
    """Frequency sweep f0 -> f1 with a phase accumulator."""
    out, phase = [], 0.0
    n = int(length * SR)
    for i in range(n):
        t = i / SR
        k = (t / length) ** curve
        f = f0 + (f1 - f0) * k
        phase += f / SR
        out.append(wave_fn(phase) * env(t, length) * vol)
    return out


def noise_sweep(length, hold0, hold1, vol=0.5, seed=1, attack=0.01):
    """Filtered-ish noise: the hold length sweeps (high -> low pitch)."""
    rng = random.Random(seed)
    out, v, cnt = [], 0.0, 0
    n = int(length * SR)
    for i in range(n):
        t = i / SR
        hold = max(1, int(hold0 + (hold1 - hold0) * t / length))
        if cnt <= 0:
            v = rng.uniform(-1, 1)
            cnt = hold
        cnt -= 1
        out.append(v * env(t, length, attack) * vol)
    return out


def tones(seq, wave_fn=square, vol=0.45, gap=0.0):
    """Sequence of (freq, seconds) notes."""
    out = []
    for f, d in seq:
        phase = 0.0
        for i in range(int(d * SR)):
            t = i / SR
            phase += f / SR
            out.append(wave_fn(phase) * env(t, d, 0.003, d * 0.4) * vol)
        out += [0.0] * int(gap * SR)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    return [sum(p[i] for p in parts if i < len(p)) for i in range(n)]


# --- the clips ---------------------------------------------------------------

def whoosh():
    return noise_sweep(0.32, 2, 9, vol=0.55, seed=3, attack=0.06)


def stall():
    # engine coughing out: falling square with stutter
    s = sweep(0.45, 220, 70, lambda p: square(p, 0.3), vol=0.45, curve=0.7)
    return [v * (1.0 if (i // 1800) % 3 else 0.25) for i, v in enumerate(s)]


def relight():
    return mix(sweep(0.30, 80, 260, lambda p: square(p, 0.25), vol=0.4, curve=0.6),
               noise_sweep(0.18, 2, 3, vol=0.25, seed=5))


def confirm():
    return tones([(660, 0.05), (990, 0.09)], vol=0.4)


def whistle():
    return sweep(0.9, 1500, 600, tri, vol=0.35, curve=1.3)


def beep():
    return tones([(1320, 0.06)], vol=0.35)


def lock():
    return tones([(1760, 0.05), (0, 0.03), (1760, 0.05), (0, 0.03), (2093, 0.10)], vol=0.3)


def siren():
    out, phase = [], 0.0
    length = 1.4
    for i in range(int(length * SR)):
        t = i / SR
        f = 700 + 300 * math.sin(2 * math.pi * 1.6 * t)
        phase += f / SR
        out.append(tri(phase) * env(t, length, 0.05, 0.3) * 0.4)
    return out


def emp():
    return mix(sweep(0.7, 1200, 40, lambda p: square(p, 0.5), vol=0.35, curve=0.4),
               noise_sweep(0.7, 1, 12, vol=0.3, seed=9))


def nitro():
    return mix(noise_sweep(0.55, 1, 3, vol=0.45, seed=11, attack=0.02),
               sweep(0.55, 120, 420, lambda p: square(p, 0.2), vol=0.2))


def roll():
    return noise_sweep(0.45, 6, 1, vol=0.5, seed=13, attack=0.05)


def clap():
    return mix(noise_sweep(0.35, 1, 6, vol=0.7, seed=17, attack=0.002),
               sweep(0.25, 160, 40, tri, vol=0.5))


def supply():
    return tones([(523, 0.08), (659, 0.08), (784, 0.08), (1047, 0.18)], vol=0.4)


def special():
    return tones([(392, 0.06), (523, 0.06), (784, 0.06), (1047, 0.06), (1568, 0.16)], tri, vol=0.5)


def tab():
    return tones([(880, 0.03)], vol=0.3)


CLIPS = {
    "whoosh": whoosh, "stall": stall, "relight": relight, "confirm": confirm,
    "whistle": whistle, "beep": beep, "lock": lock, "siren": siren, "emp": emp,
    "nitro": nitro, "roll": roll, "clap": clap, "supply": supply,
    "special": special, "tab": tab,
}


def write(key, samples):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"sfx_{key}.wav"
    frames = bytearray()
    for v in samples:
        v = max(-0.98, min(0.98, v))
        frames += int(v * 32767).to_bytes(2, "little", signed=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))
    meta = path.with_suffix(".wav.meta")
    if not meta.exists():
        meta.write_text(META.format(guid=uuid.uuid4().hex))
    print(f"{path.name}: {len(samples) / SR:.2f}s")


def main(argv):
    keys = argv or list(CLIPS)
    for k in keys:
        write(k, CLIPS[k]())


if __name__ == "__main__":
    main(sys.argv[1:])
