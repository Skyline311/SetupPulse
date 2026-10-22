import { useState } from "react";
import type { DisplayDevice, DisplaySetting } from "@/lib/bridge";

const standard = [
  [5120, 1440],
  [3840, 2160],
  [3440, 1440],
  [2560, 1440],
  [2560, 1080],
  [1920, 1200],
  [1920, 1080],
  [1680, 1050],
  [1600, 900],
  [1440, 900],
  [1366, 768],
  [1280, 1024],
  [1280, 720],
  [1024, 768],
].map(([width, height]) => ({
  width,
  height,
  refreshRate: 60,
  recommended: false,
}));
function aspect(width: number, height: number) {
  const ratios = [
    [16, 9],
    [16, 10],
    [4, 3],
    [5, 4],
    [21, 9],
    [32, 9],
    [3, 2],
    [1, 1],
    [9, 16],
    [10, 16],
    [3, 4],
    [4, 5],
    [9, 21],
  ];
  const match = ratios.find(
    ([w, h]) =>
      Math.abs(width / height - w / h) < (w === 21 || h === 21 ? 0.07 : 0.015),
  );
  if (match) return match.join(":");
  const gcd = (a: number, b: number): number => (b ? gcd(b, a % b) : a);
  const divisor = gcd(width, height) || 1;
  return `${width / divisor}:${height / divisor}`;
}
export function ResolutionPicker({
  device,
  item,
  onChange,
}: {
  device: DisplayDevice;
  item: DisplaySetting;
  onChange: (patch: Partial<DisplaySetting>) => void;
}) {
  const portrait = item.rotation === 2 || item.rotation === 4;
  const reported = device.modes || [];
  const nativeSizes = new Set(reported.map((m) => `${m.width}x${m.height}`));
  const modes = [
    ...reported.map((m) => ({ ...m, reported: true })),
    ...standard
      .filter((m) => !nativeSizes.has(`${m.width}x${m.height}`))
      .map((m) => ({ ...m, reported: false })),
  ].map((m) => ({
    ...m,
    width: portrait ? m.height : m.width,
    height: portrait ? m.width : m.height,
  }));
  const options = Array.from(
    new Set(modes.map((m) => `${m.width}x${m.height}`)),
  )
    .map((id) => {
      const matches = modes.filter((m) => `${m.width}x${m.height}` === id);
      return {
        id,
        width: matches[0].width,
        height: matches[0].height,
        recommended: matches.some((m) => m.recommended),
        reported: matches.some((m) => m.reported),
        modes: matches,
      };
    })
    .sort((a, b) => b.width * b.height - a.width * a.height);
  const current = `${item.width}x${item.height}`;
  const [manual, setManual] = useState(false);
  const custom = manual || !options.some((o) => o.id === current);
  return (
    <div className="stack mt-3">
      <div className="field">
        <label>Auflösung</label>
        <select
          aria-label={`${device.name} Auflösung`}
          value={custom ? "custom" : current}
          onChange={(e) => {
            if (e.target.value === "custom") {
              setManual(true);
              return;
            }
            setManual(false);
            const chosen = options.find((o) => o.id === e.target.value)!;
            const rate = chosen.modes.some(
              (m) => Math.abs(m.refreshRate - item.refreshRate) < 0.15,
            )
              ? item.refreshRate
              : (
                  chosen.modes.find((m) => m.recommended) ||
                  chosen.modes.find(
                    (m) => Math.abs(m.refreshRate - 60) < 0.15,
                  ) ||
                  chosen.modes[0]
                ).refreshRate;
            onChange({
              width: chosen.width,
              height: chosen.height,
              refreshRate: rate,
            });
          }}
        >
          {[true, false].map(
            (reported) =>
              options.some((o) => o.reported === reported) && (
                <optgroup
                  key={String(reported)}
                  label={
                    reported ? "Vom Monitor gemeldet" : "Weitere Standardgrößen"
                  }
                >
                  {options
                    .filter((o) => o.reported === reported)
                    .map((o) => (
                      <option key={o.id} value={o.id}>
                        {o.width} × {o.height} · {aspect(o.width, o.height)}
                        {o.recommended ? " (empfohlen)" : ""}
                      </option>
                    ))}
                </optgroup>
              ),
          )}
          <option value="custom">Benutzerdefiniert …</option>
        </select>
      </div>
      {custom && (
        <div className="field-grid">
          {(["width", "height"] as const).map((key, i) => (
            <div key={key} className="field">
              <label>{i ? "Höhe (Pixel)" : "Breite (Pixel)"}</label>
              <input
                aria-label={`${device.name} ${key}`}
                type="number"
                min={i ? 200 : 320}
                max="16384"
                value={item[key]}
                onChange={(e) => onChange({ [key]: Number(e.target.value) })}
              />
            </div>
          ))}
        </div>
      )}
      <span className="helper">
        {custom
          ? `${item.width} × ${item.height} · ${aspect(item.width, item.height)} · eigene Pixelgröße`
          : "Eigene Werte über „Benutzerdefiniert“. Weitere Standardgrößen prüft Windows beim Aktivieren."}
      </span>
    </div>
  );
}
