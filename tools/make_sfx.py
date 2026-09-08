#!/usr/bin/env python3
"""Synthesize the weapon sound effects (CC0, self-made) into assets/audio/*.wav — 44.1 kHz mono 16-bit.
One short one-shot per weapon fire plus the Firework Gun shell burst. Run after changing a recipe; re-import in Godot.
usage: tools/make_sfx.py [outdir=assets/audio]"""
import sys, os, wave, numpy as np

SR = 44100
rng = np.random.default_rng(7)

def t(sec): return np.arange(int(SR * sec)) / SR
def env(sec, attack=0.002, decay=0.1, curve=1.0):
    """attack ramp then exponential decay; decay = time constant in seconds"""
    x = t(sec); e = np.exp(-np.maximum(x - attack, 0) / decay) ** curve
    e[x < attack] = (x[x < attack] / attack)
    return e
def noise(sec): return rng.standard_normal(int(SR * sec))
def sweep(sec, f0, f1, tau):
    """sine whose frequency falls from f0 toward f1 with time constant tau"""
    x = t(sec); f = f1 + (f0 - f1) * np.exp(-x / tau)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)
def lowpass(x, fc):
    a = np.exp(-2 * np.pi * fc / SR); y = np.empty_like(x); acc = 0.0
    for i in range(len(x)): acc = a * acc + (1 - a) * x[i]; y[i] = acc
    return y
def highpass(x, fc): return x - lowpass(x, fc)
def bandpass(x, lo, hi): return highpass(lowpass(x, hi), lo)
def fit(a, n):
    out = np.zeros(n); m = min(n, len(a)); out[:m] = a[:m]; return out
def mix(*parts):
    n = max(len(p) for p in parts); return sum(fit(p, n) for p in parts)
def reverb(x, taps=((0.031, 0.5), (0.057, 0.35), (0.089, 0.25), (0.131, 0.15)), tail=0.25):
    n = len(x) + int(SR * tail); y = fit(x, n)
    for d, g in taps:
        k = int(SR * d); y[k:] += g * fit(x, n)[:n - k]
    return y
def clip_soft(x, drive=1.0): return np.tanh(x * drive)
def normalize(x, peak=0.9):
    m = np.max(np.abs(x)); return x * (peak / m) if m > 0 else x
def write(name, x, outdir):
    x = normalize(x); x = x * np.concatenate([np.ones(len(x) - 200), np.linspace(1, 0, 200)])   # fade the last 5 ms
    with wave.open(os.path.join(outdir, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())
    print(f"{name}.wav  {len(x)/SR:.2f}s")

# ---------------------------------------------------------------- recipes
def rifle():
    """QBZ-191: dry, sharp crack with a short low thump"""
    crack = bandpass(noise(0.16), 900, 6000) * env(0.16, 0.001, 0.022)
    body  = bandpass(noise(0.16), 150, 1200) * env(0.16, 0.002, 0.045) * 0.7
    thump = sweep(0.12, 190, 60, 0.03) * env(0.12, 0.001, 0.035) * 0.9
    return reverb(clip_soft(mix(crack, body, thump), 1.6), tail=0.12)

def shotgun():
    """Saiga-12K: wide boom, heavy low end, longer decay"""
    blast = bandpass(noise(0.4), 300, 4500) * env(0.4, 0.001, 0.06)
    low   = lowpass(noise(0.4), 350) * env(0.4, 0.002, 0.13) * 2.2
    thump = sweep(0.3, 140, 38, 0.05) * env(0.3, 0.001, 0.09) * 1.4
    return reverb(clip_soft(mix(blast, low, thump), 1.9), tail=0.3)

def smg():
    """Frost SR-3M: light quick snap with a crystalline ping — quieter, it fires 12/s"""
    snap  = bandpass(noise(0.07), 1500, 8000) * env(0.07, 0.0005, 0.010)
    ping  = (np.sin(2*np.pi*2600*t(0.09)) + 0.6*np.sin(2*np.pi*3900*t(0.09)) + 0.3*np.sin(2*np.pi*5200*t(0.09))) * env(0.09, 0.001, 0.02) * 0.35
    tick  = sweep(0.05, 420, 160, 0.015) * env(0.05, 0.001, 0.012) * 0.6
    return clip_soft(mix(snap, ping, tick), 1.3) * 0.75

def sniper():
    """TS-128: a charged rising zap, then a heavy crack with an electric buzz tail"""
    x = t(0.07); chirp = np.sin(2*np.pi*np.cumsum(400 + 2600 * (x / 0.07) ** 2) / SR) * env(0.07, 0.003, 0.05) * 0.5
    crack = np.concatenate([np.zeros(int(SR*0.065)), bandpass(noise(0.35), 500, 7000) * env(0.35, 0.001, 0.05)])
    thump = np.concatenate([np.zeros(int(SR*0.065)), sweep(0.3, 160, 45, 0.05) * env(0.3, 0.001, 0.09) * 1.5])
    bz = t(0.3); buzz = np.sign(np.sin(2*np.pi*95*bz)) * (0.5 + 0.5*np.sin(2*np.pi*31*bz)) * env(0.3, 0.005, 0.07) * 0.35
    buzz = bandpass(np.concatenate([np.zeros(int(SR*0.07)), buzz]), 200, 3000)
    return reverb(clip_soft(mix(chirp, crack, thump, buzz), 1.7), tail=0.35)

def firework():
    """Firework Gun: a soft 'thoomp' launch with a whoosh"""
    thoomp = sweep(0.28, 230, 65, 0.06) * env(0.28, 0.006, 0.08) * 1.3
    puff   = lowpass(noise(0.28), 900) * env(0.28, 0.004, 0.06) * 1.1
    x = t(0.4); whoosh = bandpass(noise(0.4), 600, 2500) * np.sin(np.pi * np.clip(x / 0.4, 0, 1)) ** 2 * 0.35
    return reverb(clip_soft(mix(thoomp, puff, whoosh), 1.4), tail=0.2)

def explosion():
    """Firework shell burst: deep boom, long noisy decay, sparkling crackle"""
    boom  = sweep(0.7, 110, 28, 0.12) * env(0.7, 0.002, 0.22) * 1.6
    body  = lowpass(noise(0.9), 1800) * env(0.9, 0.001, 0.18) * 1.5
    sub   = lowpass(noise(0.9), 200) * env(0.9, 0.003, 0.3) * 2.0
    crackle = np.zeros(int(SR*0.8))
    for k in rng.integers(int(SR*0.05), int(SR*0.75), 60):
        crackle[k:k+40] += rng.uniform(0.3, 1.0) * np.exp(-np.arange(40)/8) * (1 - k/len(crackle))
    crackle = bandpass(crackle, 1500, 9000) * 1.2
    return reverb(clip_soft(mix(boom, body, sub, crackle), 1.8), taps=((0.041, 0.5), (0.077, 0.4), (0.119, 0.3), (0.181, 0.2)), tail=0.5)

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "assets/audio"
    os.makedirs(out, exist_ok=True)
    for name, fn in [("shot_rifle", rifle), ("shot_shotgun", shotgun), ("shot_smg", smg), ("shot_sniper", sniper), ("shot_firework", firework), ("explosion", explosion)]:
        write(name, fn(), out)
