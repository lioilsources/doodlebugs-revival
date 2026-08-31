#!/usr/bin/env python3
"""Two seamless 8-bit music loops, stdlib only (wave + math — no numpy here).

Same tradition as the procedural SFX: the whole soundtrack is code, so a
style change is a re-run, not an asset hunt. Both loops are written to
Assets/Doodlebugs/Resources/Music/ and are whole-bar long with every voice
gated to note boundaries, which is what makes the loop seam inaudible.

  music_hangar.wav — calm triangle arpeggio over a soft square bass, 90 BPM
  music_battle.wav — driving square lead + noise hats + bass, 140 BPM
"""
import math
import random
import wave
from pathlib import Path

SR = 44100
OUT = Path(__file__).parents[2] / "Assets/Doodlebugs/Resources/Music"

def note(n):
    """MIDI note number -> frequency."""
    return 440.0 * 2 ** ((n - 69) / 12)

def square(t, f, duty=0.5):
    return 1.0 if (t * f) % 1.0 < duty else -1.0

def triangle(t, f):
    p = (t * f) % 1.0
    return 4 * p - 1 if p < 0.5 else 3 - 4 * p

def render(length_s, voices):
    """Mix voice callables (t -> sample) into 16-bit mono frames."""
    n = int(length_s * SR)
    frames = bytearray()
    for i in range(n):
        t = i / SR
        s = sum(v(t) for v in voices)
        s = max(-0.98, min(0.98, s))
        frames += int(s * 32767).to_bytes(2, "little", signed=True)
    return bytes(frames)

def gate(t, t0, t1, attack=0.004, release=0.03):
    """Note envelope: quick attack, gentle release, zero outside [t0, t1]."""
    if t < t0 or t >= t1:
        return 0.0
    a = min(1.0, (t - t0) / attack)
    r = min(1.0, (t1 - t) / release)
    return a * r

def seq_voice(pattern, step_s, wave_fn, vol, duty=None):
    """pattern: list of MIDI notes (None = rest), one per step, looped."""
    total = len(pattern) * step_s
    def v(t):
        tl = t % total
        idx = int(tl / step_s)
        n = pattern[idx]
        if n is None:
            return 0.0
        t0 = idx * step_s
        env = gate(tl, t0, t0 + step_s * 0.9)
        f = note(n)
        s = wave_fn(tl, f) if duty is None else wave_fn(tl, f, duty)
        return s * vol * env
    return v

def noise_voice(pattern, step_s, vol):
    rnd = random.Random(1917)
    burst = [rnd.uniform(-1, 1) for _ in range(2048)]
    total = len(pattern) * step_s
    def v(t):
        tl = t % total
        idx = int(tl / step_s)
        if not pattern[idx]:
            return 0.0
        t0 = idx * step_s
        env = gate(tl, t0, t0 + step_s * 0.25, attack=0.001, release=0.02)
        return burst[int(tl * SR) % 2048] * vol * env
    return v

def write(name, data):
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)
    print(f"{name}: {len(data) // 2 / SR:.2f}s")

def hangar():
    # 90 BPM, 4 bars of Am - F - C - G, eighth-note arpeggio.
    step = 60 / 90 / 2                     # eighth note
    A, F, C, G = 57, 53, 48, 55            # chord roots (A3, F3, C3, G3)
    def arp(root, third, fifth):
        return [root + 12, third + 12, fifth + 12, third + 12,
                root + 24, third + 12, fifth + 12, third + 12]
    lead = arp(A, 60, 64) + arp(F, 57, 60) + arp(C, 52, 55) + arp(G, 59, 62)
    bass = []
    for r in (A, F, C, G):
        bass += [r - 12, None, r - 12, None, r - 12, None, r - 5, None]
    length = len(lead) * step               # 32 eighths at 90 BPM = 10.667s… x2 below
    voices = [
        seq_voice(lead * 2, step, triangle, 0.28),
        seq_voice(bass * 2, step, square, 0.16, duty=0.25),
    ]
    write("music_hangar.wav", render(length * 2, voices))

def battle():
    # 140 BPM, 4 bars of Em - C - D - Em, driving sixteenth feel on hats.
    step = 60 / 140 / 2
    lead = [64, 67, 71, 67,  64, 67, 72, 71,
            60, 64, 67, 64,  60, 64, 69, 67,
            62, 66, 69, 66,  62, 66, 71, 69,
            64, 67, 71, 74,  71, 67, 64, 62]
    bass = []
    for r in (40, 36, 38, 40):
        bass += [r, r, None, r,  r, None, r, r]
    hats = [1, 0, 1, 1] * 8
    length = len(lead) * step
    voices = [
        seq_voice(lead * 2, step, square, 0.20, duty=0.5),
        seq_voice(bass * 2, step, square, 0.18, duty=0.25),
        noise_voice(hats * 2, step, 0.10),
    ]
    write("music_battle.wav", render(length * 2, voices))

if __name__ == "__main__":
    hangar()
    battle()
