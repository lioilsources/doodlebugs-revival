#!/usr/bin/env python3
"""Element sound effects - 24 clips from the ElevenLabs sound-generation API.

The matrix (plan 24, D6): shoot per (element, form GROUP) where the groups are
`gun` (tracer, pellet, bolt) and `heavy` (bomb, rocket, mine) = 12, impact 6,
explosion 6 -> 24 clips, 3 candidates each = 72 generations. Per-form shoot
would be 48 clips for a difference nobody hears under Bullet's +-8 % pitch
jitter.

  render  POST /v1/sound-generation per (key, candidate) -> out/raw/<key>_<n>.mp3,
          cached and resumable. Needs $ELEVENLABS_API_KEY (Bitwarden:
          CI / ELEVENLABS_API_KEY) - never committed, never defaulted.
  post    ffmpeg: decode -> mono 44.1 kHz 16-bit WAV (what SfxManager loads and
          what the eight procedural clips already in Resources/Sfx are), trim
          silence at -50 dBFS both ends, peak-normalise to -1 dBFS, hard length
          cap per event, optional --bitcrush. No network.
  sheet   sfx.html: an <audio> per candidate next to today's generic clip, with
          an RMS loudness bar so a limp pick is obvious before it reaches the
          game.
  apply   --pick fire.shoot_gun=2 -> Resources/Sfx/Elements/<element>/ + meta.

The bit-crusher and the length caps are not optional if D3 holds: ElevenLabs
leans cinematic, and a 1.5 s reverb tail next to an 8-bit blip sounds like two
different games.

Usage:
  export ELEVENLABS_API_KEY=...          # Bitwarden: CI / ELEVENLABS_API_KEY
  python3 tools/weapons/generate_sfx.py render --elements batch1
  python3 tools/weapons/generate_sfx.py render --dry-run          # prompts + cost only
  python3 tools/weapons/generate_sfx.py post --bitcrush
  python3 tools/weapons/generate_sfx.py sheet && open tools/weapons/sfx.html
  python3 tools/weapons/generate_sfx.py apply --pick fire.shoot_gun=2,fire.explosion=1
"""
import argparse
import array
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
import wave
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ASSETS_DIR = ROOT / "Assets/Doodlebugs/Resources/Sfx/Elements"
# The procedural clips the element set has to sit next to, not on top of.
GENERIC_SFX_DIR = ROOT / "Assets/Doodlebugs/Resources/Sfx"
GENERIC_SFX = ROOT / "Assets/Doodlebugs/Resources/Sfx"
OUT = HERE / "out"
RAW = OUT / "sfx_raw"
WAVS = OUT / "sfx"

sys.path.insert(0, str((ROOT / "tools/planes").resolve()))
import unity_meta as UM  # noqa: E402

sys.path.insert(0, str(HERE))
import elements as E  # noqa: E402
import forms as F  # noqa: E402

API = "https://api.elevenlabs.io/v1/sound-generation"
KEY_ENV = "ELEVENLABS_API_KEY"
FFMPEG = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
CANDIDATES = 3
RATE = 44100                   # matches every clip already in Resources/Sfx
PROMPT_INFLUENCE = 0.6

# event -> (asset filename, generation length, hard cap after trimming). The
# generation length is deliberately longer than the cap: ElevenLabs pads the
# front, and asking for 0.25 s gets a clip that is mostly attack ramp.
EVENTS = {
    "shoot_gun": dict(file="sfx_shoot_gun.wav", gen=0.6, cap=0.25,
                      what="a single quick gunshot firing"),
    "shoot_heavy": dict(file="sfx_shoot_heavy.wav", gen=0.8, cap=0.25,
                        what="a single heavy weapon launching, a deep thump"),
    "impact": dict(file="sfx_impact.wav", gen=0.8, cap=0.40,
                   what="a single small projectile hitting and splashing"),
    "explosion": dict(file="sfx_explosion.wav", gen=1.5, cap=0.90,
                      what="a single explosion blast"),
}
# The generic clip each element clip falls back to in SfxManager - the review
# sheet plays it next to the candidates so the new set does not drift louder or
# longer than what the game already sounds like.
REFERENCE = {"shoot_gun": "sfx_shoot.wav", "shoot_heavy": "sfx_shoot.wav",
             "impact": "sfx_hit_hull.wav", "explosion": "sfx_explosion.wav"}
# forms.py owns the grouping. If a third group is ever added there, this trips
# on import instead of quietly shipping 24 clips that cover two thirds of it.
assert {f"shoot_{g}" for g in F.GROUPS} <= set(EVENTS), \
    f"forms.GROUPS {F.GROUPS} has no matching EVENTS entry"

TRIM_DB = -50                  # silence floor for the trim, both ends
PEAK_DB = -1.0                 # peak-normalise target
CRUSH_RATE = 11025             # --bitcrush: resample down to this and back
CRUSH_FMT = "u8"               #             at 8-bit depth


# ------------------------------------------------------------------ keys --
def key_of(element, event):
    return f"{element}__{event}"


def parse_key(key):
    """'fire__shoot_gun' or the --pick spelling 'fire.shoot_gun'."""
    element, event = key.replace(".", "__").split("__", 1)
    return element, event


def prompt_for(element, event):
    """flavour x event x the shared 8-bit tail."""
    spec = EVENTS[event]
    return f"{spec['what']}, {E.sfx_flavour(element)}. {E.SFX_STYLE}"


def pick_events(arg):
    if not arg:
        return list(EVENTS)
    names = arg.split(",")
    unknown = [n for n in names if n not in EVENTS]
    if unknown:
        sys.exit(f"unknown event(s) {unknown}; have {list(EVENTS)}")
    return names


def matrix(a):
    return [(e, ev) for e in E.keys(a.elements) for ev in pick_events(a.events)]


# ---------------------------------------------------------------- render --
def api_key():
    key = os.environ.get(KEY_ENV, "").strip()
    if not key:
        sys.exit(
            f"{KEY_ENV} is not set.\n"
            f"  The ElevenLabs key lives in Bitwarden as `CI / ELEVENLABS_API_KEY`\n"
            f"  (same convention as the signing secrets). Export it for this shell:\n"
            f"      export {KEY_ENV}=$(bw get password 'CI / ELEVENLABS_API_KEY')\n"
            f"  `render` is the only subcommand that needs it - post, sheet and\n"
            f"  apply run entirely off out/sfx_raw.")
    return key


def generate(key, prompt, duration):
    req = urllib.request.Request(
        API,
        data=json.dumps({"text": prompt, "duration_seconds": duration,
                         "prompt_influence": PROMPT_INFLUENCE}).encode(),
        headers={"xi-api-key": key, "Content-Type": "application/json",
                 "Accept": "audio/mpeg"})
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return r.read()
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")[:400]
        raise RuntimeError(f"HTTP {e.code}: {body}") from None


def cmd_render(a):
    wanted = []
    for element, event in matrix(a):
        for n in range(a.candidates):
            key = key_of(element, event)
            dst = RAW / f"{key}_{n}.mp3"
            if dst.exists() and not a.force:
                continue
            wanted.append((element, event, n, dst))
    if a.dry_run:
        seen = set()
        for element, event, n, _dst in wanted:
            if (element, event) in seen:
                continue
            seen.add((element, event))
            print(f"## {key_of(element, event)}  {EVENTS[event]['gen']}s\n"
                  f"{prompt_for(element, event)}\n")
        print(f"{len(wanted)} generation(s) over {len(seen)} clip(s)")
        return
    if not wanted:
        print("nothing to render (all cached) - use `post` to redo the ffmpeg steps")
        return

    key = api_key()
    RAW.mkdir(parents=True, exist_ok=True)
    for i, (element, event, n, dst) in enumerate(wanted, 1):
        try:
            dst.write_bytes(generate(key, prompt_for(element, event),
                                     EVENTS[event]["gen"]))
            print(f"[{i}/{len(wanted)}] {dst.name}  {dst.stat().st_size // 1024} kB",
                  flush=True)
        except Exception as exc:  # noqa: BLE001 - keep the batch going
            print(f"[FAIL] {dst.name}: {exc}", flush=True)


# ------------------------------------------------------------------ post --
def ffmpeg(args):
    p = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", *args],
                       capture_output=True, text=True)
    if p.returncode:
        raise RuntimeError(p.stderr.strip()[:400])
    return p


def peak_db(path):
    """max_volume from ffmpeg's volumedetect, in dBFS (0 = full scale)."""
    p = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path), "-af", "volumedetect",
                        "-f", "null", "-"], capture_output=True, text=True)
    m = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?) dB", p.stderr)
    return float(m.group(1)) if m else 0.0


TRIM = (f"silenceremove=start_periods=1:start_threshold={TRIM_DB}dB:"
        f"start_silence=0:detection=peak,areverse,"
        f"silenceremove=start_periods=1:start_threshold={TRIM_DB}dB:"
        f"start_silence=0:detection=peak,areverse")


_REF_RMS_CACHE = {}


def reference_rms(event):
    """RMS of the generic clip this event falls back to, measured from the
    clip actually in Resources/Sfx.

    Peak normalising alone is not enough: peak says nothing about how loud a
    thing SOUNDS, and the ElevenLabs renders are far denser than the
    procedural blips. Peak-normalised, the new explosions measured -10 dBFS
    against the existing -18.6 - eight decibels louder, on the event that
    already plays at 0.9 volume. So the target is the reference clip's RMS,
    and the peak ceiling only ever pulls it further down."""
    if event in _REF_RMS_CACHE:
        return _REF_RMS_CACHE[event]
    ref = GENERIC_SFX_DIR / REFERENCE[event]
    rms = wav_stats(ref)[1] if ref.exists() else None
    _REF_RMS_CACHE[event] = rms
    return rms


def post_one(src, dst, cap, bitcrush=False, event=None):
    """mp3 -> mono 44.1 kHz 16-bit WAV, trimmed, loudness-matched, capped.

    Two ffmpeg passes because the gain needs the measurement first:
    volumedetect + an RMS pass on the trimmed signal, then a fixed `volume`
    gain. loudnorm would be the LUFS answer and it is the wrong one here -
    these are 200 ms transients, and loudnorm's gating would pump them."""
    tmp = dst.with_suffix(".trim.wav")
    ffmpeg(["-i", str(src), "-ac", "1", "-ar", str(RATE), "-c:a", "pcm_s16le",
            "-af", f"{TRIM},atrim=0:{cap}", str(tmp)])

    headroom = PEAK_DB - peak_db(tmp)          # never clip
    target = reference_rms(event) if event else None
    if target is None:
        gain = headroom
    else:
        # Match the generic clip's loudness, but never push past the ceiling.
        gain = min(headroom, target - wav_stats(tmp)[1])

    chain = f"volume={gain:.2f}dB"
    if bitcrush:
        # 8-bit at 11 kHz and back: what makes an ElevenLabs render sit next to
        # the procedural blips instead of on top of them.
        chain += (f",aresample={CRUSH_RATE},aformat=sample_fmts={CRUSH_FMT},"
                  f"aformat=sample_fmts=s16,aresample={RATE}")
    ffmpeg(["-i", str(tmp), "-ac", "1", "-ar", str(RATE), "-c:a", "pcm_s16le",
            "-af", chain, str(dst)])
    tmp.unlink(missing_ok=True)
    return gain


def wav_stats(path):
    """(seconds, rms_dbfs, peak_dbfs) from the stdlib - audioop was removed in
    Python 3.13, so the sums are done here."""
    with wave.open(str(path), "rb") as w:
        n, rate, width = w.getnframes(), w.getframerate(), w.getsampwidth()
        data = w.readframes(n)
    if width != 2 or not n:
        return (n / rate if rate else 0.0), -99.0, -99.0
    samples = array.array("h")
    samples.frombytes(data)
    sq = 0
    peak = 0
    for s in samples:
        sq += s * s
        if abs(s) > peak:
            peak = abs(s)
    import math
    rms = math.sqrt(sq / len(samples)) / 32768.0
    return (n / rate,
            20 * math.log10(rms) if rms > 0 else -99.0,
            20 * math.log10(peak / 32768.0) if peak else -99.0)


def cmd_post(a):
    WAVS.mkdir(parents=True, exist_ok=True)
    want = {key_of(e, ev) for e, ev in matrix(a)}
    rows, n = {}, 0
    for src in sorted(RAW.glob("*.mp3")):
        key, cand = src.stem.rsplit("_", 1)
        if key not in want:
            continue
        _element, event = parse_key(key)
        dst = WAVS / f"{key}_{cand}.wav"
        try:
            gain = post_one(src, dst, EVENTS[event]["cap"], a.bitcrush, event)
        except RuntimeError as exc:
            print(f"[FAIL] {src.name}: {exc}")
            continue
        dur, rms, peak = wav_stats(dst)
        rows.setdefault(key, {})[cand] = dict(dur=dur, rms=rms, peak=peak, gain=gain)
        print(f"{key:24s} #{cand}  {dur:.2f}s  rms {rms:6.1f} dBFS  peak {peak:5.1f}  "
              f"gain {gain:+.1f} dB{'  crushed' if a.bitcrush else ''}")
        n += 1
    if rows:
        OUT.mkdir(parents=True, exist_ok=True)
        old = json.loads((OUT / "sfx.json").read_text()) if (OUT / "sfx.json").exists() else {}
        old.update(rows)
        (OUT / "sfx.json").write_text(json.dumps(old, indent=1))
    print(f"post: {n} clip(s) -> {WAVS}")


# ----------------------------------------------------------------- sheet --
def load_stats():
    p = OUT / "sfx.json"
    return json.loads(p.read_text()) if p.exists() else {}


def cmd_sheet(_a):
    stats = load_stats()
    sections = []
    for element in E.ELEMENTS:
        rows = []
        for event in EVENTS:
            key = key_of(element, event)
            got = stats.get(key, {})
            cards = []
            for cand in sorted(got):
                s = got[cand]
                # RMS bar: -40 dBFS empty, 0 dBFS full. A candidate that reads
                # half the width of its neighbours will be inaudible in a
                # dogfight no matter how good it sounds on headphones.
                pct = max(0.0, min(1.0, (s["rms"] + 40) / 40)) * 100
                cards.append(
                    f'<div class=cand><b>#{cand}</b> '
                    f'<audio controls preload=none src="out/sfx/{key}_{cand}.wav"></audio>'
                    f'<div class=bar><i style="width:{pct:.0f}%"></i></div>'
                    f'<span>{s["dur"]:.2f}s &middot; rms {s["rms"]:.1f} dBFS &middot; '
                    f'peak {s["peak"]:.1f}</span></div>')
            if not cards:
                cards = ['<div class="cand none">not generated yet</div>']
            ref = REFERENCE[event]
            rows.append(
                f'<h4>{event} <small>&rarr; {EVENTS[event]["file"]}, '
                f'cap {EVENTS[event]["cap"]}s</small></h4>'
                f'<div class=cands>{"".join(cards)}'
                f'<div class="cand ref"><b>today</b>'
                f'<audio controls preload=none '
                f'src="../../Assets/Doodlebugs/Resources/Sfx/{ref}"></audio>'
                f'<span>{ref} - the fallback SfxManager plays now</span></div></div>')
        sections.append(f'<h3 style="color:{E.hex_tint(element)}">{E.get(element)["name"]} '
                        f'<small>{element} - {E.sfx_flavour(element)}</small></h3>'
                        f'{"".join(rows)}')
    html = ("<!doctype html><meta charset=utf-8><title>Doodlebugs element SFX</title>"
            "<style>body{background:#1b1e24;color:#ddd;font:14px system-ui;margin:24px}"
            ".cands{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:10px}"
            ".cand{border:2px solid #345;border-radius:8px;padding:8px;font:12px monospace}"
            ".cand.ref{border-color:#664;opacity:.8}.cand.none{border-style:dashed;opacity:.4}"
            ".bar{background:#0d1014;height:8px;border-radius:4px;margin:6px 0;overflow:hidden}"
            ".bar i{display:block;height:100%;background:#3a6}"
            "audio{width:100%;margin:4px 0}span{color:#9ab}"
            "h3{margin:32px 0 4px}h4{color:#8cf;margin:14px 0 6px;font:13px monospace}"
            "small{color:#789;font-weight:normal}</style>"
            "<h2>Element SFX</h2><p>Green bar = RMS from -40 to 0 dBFS. Compare every "
            "candidate against <b>today</b>: the new set must not be louder or longer "
            "than the 8-bit clips the game already plays.</p>" + "".join(sections))
    (HERE / "sfx.html").write_text(html)
    print(f"gallery -> {HERE / 'sfx.html'} ({sum(len(v) for v in stats.values())} clip(s))")


# ----------------------------------------------------------------- apply --
def score(s, event):
    """Lower is better: present but not blaring, and using its length budget
    rather than ending after two frames."""
    cap = EVENTS[event]["cap"]
    return abs(s["rms"] + 16.0) / 6.0 + abs(s["dur"] - cap * 0.8) / cap


def best_candidate(got, event):
    return min(got, key=lambda c: score(got[c], event)) if got else None


def cmd_apply(a):
    stats = load_stats()
    picks = {}
    if a.pick:
        for item in a.pick.split(","):
            k, val = item.split("=")
            element, event = parse_key(k)
            if event not in EVENTS:
                sys.exit(f"unknown event '{event}'; have {list(EVENTS)}")
            picks[key_of(element, event)] = val
    applied, skipped = [], []
    for element, event in matrix(a):
        key = key_of(element, event)
        got = stats.get(key, {})
        cand = picks.get(key, best_candidate(got, event))
        if cand is None:
            skipped.append(f"{element}.{event}")
            continue
        src = WAVS / f"{key}_{cand}.wav"
        if not src.exists():
            sys.exit(f"{element}.{event}: {src} missing - run `post` first")
        dst_dir = ASSETS_DIR / element
        UM.ensure_folder(dst_dir)
        dst = dst_dir / EVENTS[event]["file"]
        dst.write_bytes(src.read_bytes())
        UM.write_meta(dst, "audio")
        applied.append((element, event, cand, got.get(cand, {})))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "applied_sfx.json").write_text(json.dumps(
        {f"{e}.{ev}": c for e, ev, c, _ in applied}, indent=1))
    for element, event, cand, s in applied:
        extra = (f"{s['dur']:.2f}s rms {s['rms']:.1f} dBFS" if s else "")
        print(f"{element:10s} {event:12s} #{cand}  {extra}")
    print(f"applied {len(applied)} clip(s) -> {ASSETS_DIR}")
    if skipped:
        print(f"no candidate yet (SfxManager falls back to the generic clip): "
              f"{', '.join(skipped)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--elements", help="comma list, or 'batch1' (default: all six)")
        p.add_argument("--events", help=f"comma list of {list(EVENTS)} (default: all four)")
        return p

    p = common(sub.add_parser("render", help=f"ElevenLabs; needs ${KEY_ENV}"))
    p.add_argument("--candidates", type=int, default=CANDIDATES)
    p.add_argument("--force", action="store_true", help="re-generate cached candidates")
    p.add_argument("--dry-run", action="store_true", help="prompts and the generation count")
    p.set_defaults(fn=cmd_render)

    p = common(sub.add_parser("post", help="ffmpeg conditioning (no network)"))
    p.add_argument("--bitcrush", action="store_true",
                   help=f"8-bit at {CRUSH_RATE} Hz and back, so the clip sits next to "
                        f"the procedural 8-bit set instead of on top of it")
    p.set_defaults(fn=cmd_post)

    p = sub.add_parser("sheet"); p.set_defaults(fn=cmd_sheet)

    p = common(sub.add_parser("apply"))
    p.add_argument("--pick", help="element.event=candidate overrides, e.g. "
                                  "fire.shoot_gun=2,venom.impact=0")
    p.set_defaults(fn=cmd_apply)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
