import math
import os
import random
import struct
import uuid
import wave


RATE = 44100
ROOT = os.path.join(
    os.path.dirname(__file__),
    "..",
    "METAGAME",
    "Assets",
    "_Project",
    "Audio",
    "SFX",
)


EVENTS = [
    ("HandGrabAttempt", "SFX_Hand_Grab_Attempt", 0.22, False),
    ("HandGrabSuccess", "SFX_Hand_Grab_Success", 0.34, False),
    ("HandRelease", "SFX_Hand_Release", 0.28, False),
    ("HandThrow", "SFX_Hand_Throw", 0.42, False),
    ("ObjectLand", "SFX_Object_Land", 0.48, False),
    ("ImpactMetalMetal", "SFX_Impact_Metal_Metal", 0.38, False),
    ("ImpactMetalHard", "SFX_Impact_Metal_Hard", 0.42, False),
    ("ImpactMetalSoft", "SFX_Impact_Metal_Soft", 0.38, False),
    ("ImpactHardHard", "SFX_Impact_Hard_Hard", 0.42, False),
    ("ImpactHardSoft", "SFX_Impact_Hard_Soft", 0.40, False),
    ("ImpactSoftSoft", "SFX_Impact_Soft_Soft", 0.36, False),
    ("MeteorImpactSmall", "SFX_Meteor_Impact_Small", 0.82, False),
    ("MeteorImpactMedium", "SFX_Meteor_Impact_Medium", 1.12, False),
    ("ClampPullProgress", "SFX_Clamp_Pull_Progress", 0.22, False),
    ("TractorBoard", "SFX_Tractor_Board", 0.48, False),
    ("TractorMoveLoop", "SFX_Tractor_Move_Loop", 1.20, True),
    ("TractorTowLoop", "SFX_Tractor_Tow_Loop", 1.20, True),
    ("TractorDescendLoop", "SFX_Tractor_Descend_Loop", 1.20, True),
    ("TractorDismount", "SFX_Tractor_Dismount", 0.50, False),
    ("WeldBeamAirLoop", "SFX_Weld_Beam_Air_Loop", 1.20, True),
    ("CutBeamAirLoop", "SFX_Cut_Beam_Air_Loop", 1.20, True),
    ("WeldBeamContactLoop", "SFX_Weld_Beam_Contact_Loop", 1.20, True),
    ("WeldBeadGrowLoop", "SFX_Weld_Bead_Grow_Loop", 1.20, True),
    ("WeldBridgeGrowLoop", "SFX_Weld_Bridge_Grow_Loop", 1.20, True),
    ("WeldConfirm", "SFX_Weld_Confirm", 0.52, False),
    ("CutBeamContactLoop", "SFX_Cut_Beam_Contact_Loop", 1.20, True),
    ("WeldBeadRemoveLoop", "SFX_Weld_Bead_Remove_Loop", 1.20, True),
    ("UnweldConfirm", "SFX_Unweld_Confirm", 0.58, False),
]


def clamp(value, low=-1.0, high=1.0):
    return max(low, min(high, value))


def lerp(first, second, amount):
    return first + (second - first) * amount


def exp_decay(t, time_constant):
    return math.exp(-t / max(0.0001, time_constant))


def smoothstep(value):
    value = clamp(value, 0.0, 1.0)
    return value * value * (3.0 - 2.0 * value)


def attack_decay(t, attack, decay):
    if t < 0.0:
        return 0.0
    return (1.0 - math.exp(-t / max(0.0001, attack))) * exp_decay(t, decay)


def lowpass(signal, cutoff):
    alpha = (2.0 * math.pi * cutoff / RATE)
    alpha = alpha / (1.0 + alpha)
    output = []
    previous = 0.0
    for sample in signal:
        previous += alpha * (sample - previous)
        output.append(previous)
    return output


def highpass(signal, cutoff):
    filtered = lowpass(signal, cutoff)
    return [signal[i] - filtered[i] for i in range(len(signal))]


def bandpass(signal, low_cutoff, high_cutoff):
    return highpass(lowpass(signal, high_cutoff), low_cutoff)


def noise(count, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(count)]


def control_curve(count, rng, points=18):
    """A slowly changing control signal prevents sterile, static noise beds."""
    anchors = [rng.uniform(-1.0, 1.0) for _ in range(points + 1)]
    result = []
    for index in range(count):
        position = index / max(1, count - 1) * points
        left = min(points - 1, int(position))
        amount = smoothstep(position - left)
        result.append(lerp(anchors[left], anchors[left + 1], amount))
    return result


def phase_sweep(t, start_frequency, end_frequency, sweep_seconds):
    if t <= 0.0:
        return 0.0
    if sweep_seconds <= 0.0:
        return 2.0 * math.pi * end_frequency * t
    if t <= sweep_seconds:
        return 2.0 * math.pi * (
            start_frequency * t
            + (end_frequency - start_frequency) * t * t / (2.0 * sweep_seconds)
        )
    sweep_phase = (start_frequency + end_frequency) * sweep_seconds / 2.0
    return 2.0 * math.pi * (sweep_phase + end_frequency * (t - sweep_seconds))


def swept_tone(t, start_frequency, end_frequency, sweep_seconds, level, envelope):
    if t < 0.0:
        return 0.0
    return level * math.sin(phase_sweep(t, start_frequency, end_frequency, sweep_seconds)) * envelope


def layered_hit(t, low_frequency, high_frequency, attack, decay, body, texture,
                noise_sample, bright_sample, ring_frequency=0.0, ring_level=0.0):
    envelope = attack_decay(t, attack, decay)
    sample = swept_tone(t, low_frequency, high_frequency, decay * 0.34,
                        body, envelope)
    sample += swept_tone(t, low_frequency * 1.93, high_frequency * 1.67,
                         decay * 0.20, body * 0.34, envelope * 0.75)
    sample += noise_sample * envelope * texture
    sample += bright_sample * attack_decay(t, attack * 0.7, decay * 0.45) * texture * 0.22
    if ring_frequency > 0.0:
        sample += math.sin(2.0 * math.pi * ring_frequency * t) * \
            exp_decay(t, decay * 1.65) * ring_level
    return sample


def render_one_shot(event, seconds, style, rng):
    count = int(RATE * seconds)
    raw = noise(count, rng)
    low = lowpass(raw, 520.0 if style == "stylized" else 390.0)
    mid = bandpass(raw, 420.0, 3600.0 if style == "stylized" else 2700.0)
    bright = highpass(raw, 2300.0 if style == "stylized" else 3100.0)
    air = highpass(raw, 6200.0 if style == "stylized" else 7200.0)
    whoosh = highpass(raw, 1150.0 if style == "stylized" else 1450.0)
    drift = control_curve(count, rng)
    result = []

    impact_specs = {
        "ImpactMetalMetal": (610.0, 1120.0, 0.040, 0.19, 0.26, 0.18, 0.22),
        "ImpactMetalHard": (330.0, 720.0, 0.052, 0.23, 0.31, 0.20, 0.16),
        "ImpactMetalSoft": (190.0, 420.0, 0.060, 0.26, 0.27, 0.21, 0.11),
        "ImpactHardHard": (150.0, 310.0, 0.058, 0.25, 0.34, 0.19, 0.15),
        "ImpactHardSoft": (118.0, 250.0, 0.064, 0.27, 0.28, 0.20, 0.10),
        "ImpactSoftSoft": (82.0, 176.0, 0.074, 0.30, 0.24, 0.22, 0.06),
    }

    variation = rng.uniform(0.93, 1.07)

    for i in range(count):
        t = i / RATE
        sample = 0.0

        if event == "HandGrabAttempt":
            sample += swept_tone(t, 160.0 * variation, 320.0 * variation, 0.045,
                                 0.18, attack_decay(t, 0.004, 0.065))
            sample += swept_tone(t - 0.018, 650.0 * variation, 980.0 * variation, 0.028,
                                 0.15, attack_decay(t - 0.018, 0.002, 0.060))
            sample += mid[i] * attack_decay(t, 0.003, 0.045) * 0.20
            sample += air[i] * attack_decay(t - 0.026, 0.004, 0.055) * 0.08

        elif event == "HandGrabSuccess":
            sample += layered_hit(t, 118.0 * variation, 245.0 * variation,
                                  0.006, 0.16, 0.25, 0.18, low[i], bright[i],
                                  720.0 * variation, 0.045)
            sample += swept_tone(t - 0.028, 460.0, 900.0, 0.075, 0.10,
                                 attack_decay(t - 0.028, 0.004, 0.14))
            sample += mid[i] * attack_decay(t - 0.012, 0.004, 0.12) * 0.16

        elif event == "HandRelease":
            sample += layered_hit(t, 140.0 * variation, 92.0 * variation,
                                  0.006, 0.13, 0.20, 0.13, low[i], mid[i],
                                  420.0 * variation, 0.035)
            sample += swept_tone(t - 0.020, 880.0, 360.0, 0.07, 0.075,
                                 attack_decay(t - 0.020, 0.003, 0.11))

        elif event == "HandThrow":
            sample += whoosh[i] * 0.12 * (0.4 + 0.6 * smoothstep(min(1.0, t / 0.20))) * exp_decay(t, 0.20)
            sample += low[i] * attack_decay(t, 0.006, 0.22) * 0.15
            sample += swept_tone(t, 82.0, 145.0, 0.18, 0.12,
                                 attack_decay(t, 0.008, 0.27))
            sample += swept_tone(t - 0.12, 720.0, 280.0, 0.16, 0.06,
                                 attack_decay(t - 0.12, 0.004, 0.18))

        elif event == "ObjectLand":
            sample += layered_hit(t, 72.0 * variation, 110.0 * variation,
                                  0.007, 0.24, 0.40, 0.29, low[i], mid[i],
                                  235.0 * variation, 0.055)
            sample += swept_tone(t - 0.045, 260.0, 150.0, 0.08, 0.12,
                                 attack_decay(t - 0.045, 0.004, 0.16))
            sample += bright[i] * attack_decay(t - 0.012, 0.003, 0.09) * 0.045

        elif event in impact_specs:
            start, end, attack, decay, body, texture, ring = impact_specs[event]
            if style == "realistic":
                start *= 0.82
                end *= 0.88
                decay *= 1.18
                body *= 1.12
                texture *= 0.90
            sample += layered_hit(t, start * variation, end * variation,
                                  attack, decay, body, texture, low[i], mid[i],
                                  end * 1.48 * variation, ring)
            sample += swept_tone(t - 0.018, end * 1.7, end * 0.92, 0.09,
                                 ring * 0.60, attack_decay(t - 0.018, 0.003, decay * 0.75))
            sample += drift[i] * mid[i] * exp_decay(t, decay * 1.3) * 0.035

        elif event in ("MeteorImpactSmall", "MeteorImpactMedium"):
            medium = event == "MeteorImpactMedium"
            base = 54.0 if medium else 67.0
            tail = 0.40 if medium else 0.28
            body = 0.72 if medium else 0.54
            sample += layered_hit(t, base * variation, base * 1.36 * variation,
                                  0.010, tail, body, 0.34 if medium else 0.28,
                                  low[i], mid[i], base * 2.15, 0.09 if medium else 0.06)
            sample += low[i] * attack_decay(t - 0.035, 0.008, tail * 1.25) * \
                (0.32 if medium else 0.24)
            sample += swept_tone(t - 0.08, 190.0 if medium else 230.0,
                                 96.0 if medium else 124.0, 0.16,
                                 0.11 if medium else 0.075,
                                 attack_decay(t - 0.08, 0.006, 0.30 if medium else 0.22))
            sample += bright[i] * attack_decay(t - 0.003, 0.002, 0.12) * 0.035

        elif event == "ClampPullProgress":
            sample += layered_hit(t, 430.0 * variation, 730.0 * variation,
                                  0.003, 0.095, 0.18, 0.14, mid[i], bright[i],
                                  1260.0 * variation, 0.045)
            sample += swept_tone(t - 0.035, 170.0, 240.0, 0.09, 0.10,
                                 attack_decay(t - 0.035, 0.004, 0.11))
            sample += low[i] * attack_decay(t - 0.008, 0.003, 0.10) * 0.06

        elif event == "TractorBoard":
            sample += layered_hit(t, 86.0 * variation, 136.0 * variation,
                                  0.008, 0.22, 0.28, 0.19, low[i], mid[i],
                                  320.0 * variation, 0.06)
            sample += swept_tone(t - 0.06, 160.0, 420.0, 0.12, 0.13,
                                 attack_decay(t - 0.06, 0.008, 0.20))
            sample += bright[i] * attack_decay(t - 0.10, 0.004, 0.12) * 0.08

        elif event == "TractorDismount":
            sample += layered_hit(t, 190.0 * variation, 108.0 * variation,
                                  0.008, 0.25, 0.24, 0.18, low[i], mid[i],
                                  270.0 * variation, 0.055)
            sample += swept_tone(t - 0.055, 520.0, 270.0, 0.12, 0.09,
                                 attack_decay(t - 0.055, 0.004, 0.18))

        elif event == "WeldConfirm":
            sample += swept_tone(t, 280.0 * variation, 510.0 * variation, 0.17,
                                 0.18, attack_decay(t, 0.006, 0.26))
            sample += swept_tone(t - 0.10, 620.0 * variation, 980.0 * variation,
                                 0.16, 0.14, attack_decay(t - 0.10, 0.005, 0.24))
            sample += mid[i] * attack_decay(t - 0.045, 0.004, 0.20) * 0.08
            sample += bright[i] * attack_decay(t - 0.16, 0.003, 0.16) * 0.05

        elif event == "UnweldConfirm":
            sample += swept_tone(t, 920.0 * variation, 470.0 * variation, 0.20,
                                 0.15, attack_decay(t, 0.006, 0.30))
            sample += swept_tone(t - 0.08, 470.0 * variation, 185.0 * variation,
                                 0.18, 0.20, attack_decay(t - 0.08, 0.005, 0.28))
            sample += low[i] * attack_decay(t - 0.10, 0.006, 0.30) * 0.12

        result.append(sample)

    return result


def render_footstep(seconds, rng):
    """One soft rabbit paw landing on loose soil and sand."""
    count = int(RATE * seconds)
    raw = noise(count, rng)
    soil = lowpass(raw, 920.0)
    grit = bandpass(raw, 850.0, 4300.0)
    dust = highpass(raw, 4200.0)
    drift = control_curve(count, rng, 12)
    grains = [
        (0.012, rng.uniform(1200.0, 2200.0), 0.026, 0.060),
        (0.028, rng.uniform(1900.0, 3400.0), 0.034, 0.075),
        (0.052, rng.uniform(1100.0, 2700.0), 0.048, 0.052),
        (0.086, rng.uniform(700.0, 1800.0), 0.065, 0.034),
    ]
    result = []

    for i in range(count):
        t = i / RATE
        sample = swept_tone(t, 92.0, 55.0, 0.075, 0.28,
                            attack_decay(t, 0.003, 0.135))
        sample += soil[i] * attack_decay(t, 0.004, 0.15) * (0.22 + drift[i] * 0.025)
        sample += grit[i] * attack_decay(t - 0.006, 0.002, 0.095) * 0.19
        sample += dust[i] * attack_decay(t - 0.016, 0.004, 0.075) * 0.035
        for offset, frequency, decay, level in grains:
            local = t - offset
            sample += swept_tone(local, frequency, frequency * 0.72, 0.020,
                                 level, attack_decay(local, 0.0015, decay))
        sample += swept_tone(t - 0.13, 310.0, 180.0, 0.07, 0.035,
                             attack_decay(t - 0.13, 0.004, 0.12))
        result.append(sample)

    return result


def render_loop(event, seconds, style, rng):
    count = int(RATE * seconds)
    raw = noise(count, rng)
    low = lowpass(raw, 480.0 if style == "stylized" else 340.0)
    mid = bandpass(raw, 380.0, 3300.0 if style == "stylized" else 2500.0)
    bright = highpass(raw, 2400.0 if style == "stylized" else 3200.0)
    air = highpass(raw, 6800.0 if style == "stylized" else 7600.0)
    drift = control_curve(count, rng, 26)
    result = []
    phase_offset = rng.uniform(0.0, math.pi * 2.0)
    variation = rng.uniform(0.94, 1.06)

    for i in range(count):
        t = i / RATE
        phase = t / seconds
        lfo = math.sin(2.0 * math.pi * phase + phase_offset)
        lfo_fast = math.sin(2.0 * math.pi * phase * 2.7 + phase_offset * 0.7)
        sample = 0.0

        if event == "TractorMoveLoop":
            base = (88.0 if style == "realistic" else 112.0) * variation
            sample += 0.19 * math.sin(2.0 * math.pi * base * t)
            sample += 0.08 * math.sin(2.0 * math.pi * base * 2.02 * t + 0.3)
            sample += low[i] * (0.11 + 0.025 * lfo)
            sample += mid[i] * (0.035 + 0.018 * lfo_fast)
            sample *= 0.90 + 0.10 * math.sin(2.0 * math.pi * phase * 1.3 + phase_offset)

        elif event == "TractorTowLoop":
            base = (66.0 if style == "realistic" else 82.0) * variation
            sample += 0.24 * math.sin(2.0 * math.pi * base * t)
            sample += 0.11 * math.sin(2.0 * math.pi * base * 1.51 * t + 0.6)
            sample += low[i] * (0.15 + 0.04 * lfo)
            sample += mid[i] * (0.05 + 0.02 * lfo_fast)
            sample += drift[i] * low[i] * 0.035

        elif event == "TractorDescendLoop":
            base = (96.0 if style == "realistic" else 128.0) * variation
            sample += 0.15 * math.sin(2.0 * math.pi * base * t)
            sample += 0.08 * math.sin(2.0 * math.pi * (base * 1.43 + 14.0 * lfo) * t)
            sample += low[i] * (0.10 + 0.025 * lfo)
            sample += mid[i] * 0.035
            sample += 0.045 * math.sin(2.0 * math.pi * (250.0 + 24.0 * lfo) * t)

        elif event == "WeldBeamAirLoop":
            carrier = (2050.0 if style == "stylized" else 1690.0) * variation
            sample += 0.065 * math.sin(2.0 * math.pi * carrier * t + phase_offset)
            sample += 0.055 * bright[i] * (0.82 + 0.18 * lfo_fast)
            sample += 0.030 * air[i]
            sample += 0.020 * math.sin(2.0 * math.pi * (carrier * 2.05 + 60.0 * lfo) * t)
            sample *= 0.88 + 0.12 * math.sin(2.0 * math.pi * phase * 7.0 + phase_offset)

        elif event == "CutBeamAirLoop":
            carrier = (1500.0 if style == "stylized" else 1210.0) * variation
            sample += 0.075 * math.sin(2.0 * math.pi * carrier * t + phase_offset)
            sample += 0.072 * bright[i] * (0.75 + 0.25 * lfo_fast)
            sample += 0.038 * air[i]
            sample += 0.032 * math.sin(2.0 * math.pi * (carrier * 1.73 + 80.0 * lfo) * t)
            sample *= 0.84 + 0.16 * math.sin(2.0 * math.pi * phase * 9.0 + phase_offset)

        elif event == "WeldBeamContactLoop":
            carrier = (1080.0 if style == "stylized" else 820.0) * variation
            sample += 0.050 * math.sin(2.0 * math.pi * carrier * t)
            sample += 0.072 * bright[i] * (0.70 + 0.30 * lfo_fast)
            sample += 0.052 * mid[i]
            sample += 0.026 * math.sin(2.0 * math.pi * carrier * 2.21 * t + 0.4)
            sample *= 0.86 + 0.14 * math.sin(2.0 * math.pi * phase * 5.0 + phase_offset)

        elif event == "WeldBeadGrowLoop":
            pulse = 0.5 + 0.5 * math.sin(2.0 * math.pi * phase * 8.0 + phase_offset)
            rising = 1.0 + 0.16 * math.sin(2.0 * math.pi * phase)
            sample += 0.034 * math.sin(2.0 * math.pi * 820.0 * rising * t)
            sample += (0.045 + 0.045 * pulse) * bright[i]
            sample += 0.035 * mid[i] * (0.72 + 0.28 * pulse)
            sample += 0.018 * math.sin(2.0 * math.pi * (1650.0 + pulse * 260.0) * t)

        elif event == "WeldBridgeGrowLoop":
            pulse = 0.5 + 0.5 * math.sin(2.0 * math.pi * phase * 6.0 + phase_offset)
            sample += 0.075 * math.sin(2.0 * math.pi * 260.0 * t)
            sample += (0.060 + 0.045 * pulse) * low[i]
            sample += (0.025 + 0.035 * pulse) * mid[i]
            sample += 0.022 * math.sin(2.0 * math.pi * (610.0 + pulse * 170.0) * t)

        elif event == "CutBeamContactLoop":
            pulse = 0.5 + 0.5 * math.sin(2.0 * math.pi * phase * 7.0 + phase_offset)
            sample += 0.052 * math.sin(2.0 * math.pi * 970.0 * t)
            sample += (0.050 + 0.055 * pulse) * bright[i]
            sample += (0.035 + 0.028 * pulse) * mid[i]
            sample += 0.026 * math.sin(2.0 * math.pi * (1880.0 + 180.0 * pulse) * t)

        elif event == "WeldBeadRemoveLoop":
            pulse = 0.5 + 0.5 * math.sin(2.0 * math.pi * phase * 5.0 + phase_offset)
            sample += 0.060 * math.sin(2.0 * math.pi * (430.0 - 70.0 * pulse) * t)
            sample += (0.040 + 0.050 * pulse) * mid[i]
            sample += (0.028 + 0.025 * pulse) * bright[i]
            sample += 0.018 * low[i]

        result.append(sample)

    # Equal-power-style crossfade at the seam removes a click without making the
    # loop sound like a single static oscillator.
    fade = int(RATE * 0.075)
    for i in range(fade):
        amount = smoothstep(i / max(1, fade - 1))
        tail = count - fade + i
        result[tail] = result[tail] * (1.0 - amount) + result[0] * amount
    result[-1] = result[0]
    return result


def write_wav(path, samples):
    peak = max(abs(sample) for sample in samples) or 1.0
    gain = 0.82 / peak
    pcm = bytearray()
    for sample in samples:
        value = int(clamp(sample * gain) * 32767.0)
        pcm.extend(struct.pack("<h", value))

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(RATE)
        wav_file.writeframes(pcm)


def write_unity_audio_meta(path):
    meta_path = path + ".meta"
    if os.path.exists(meta_path):
        return

    content = """fileFormatVersion: 2
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
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
""".format(guid=uuid.uuid4().hex)
    with open(meta_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def main():
    for style in ("Stylized",):
        style_dir = os.path.join(ROOT, style)
        for index, (event, filename, seconds, is_loop) in enumerate(EVENTS):
            rng = random.Random(20260902 + index * 131)
            if is_loop:
                samples = render_loop(event, seconds, style.lower(), rng)
            else:
                samples = render_one_shot(event, seconds, style.lower(), rng)
            wav_path = os.path.join(style_dir, filename + ".wav")
            write_wav(wav_path, samples)
            write_unity_audio_meta(wav_path)

    walk_rng = random.Random(20260902 + len(EVENTS) * 131)
    walk_path = os.path.join(ROOT, os.pardir, "SFX_Rabbit_Walk_Space_Light.wav")
    write_wav(os.path.abspath(walk_path), render_footstep(0.31, walk_rng))
    write_unity_audio_meta(os.path.abspath(walk_path))
    print(f"generated={len(EVENTS)} style=Stylized footstep=1 rate={RATE} "
          f"layered=attack-body-tail loops=crossfade root={os.path.abspath(ROOT)}")


if __name__ == "__main__":
    main()
