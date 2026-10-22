import { useState } from "react";
import type { DisplayDevice, DisplaySetting } from "@/lib/bridge";

const sameRate = (a: number, b: number) => Math.abs(a - b) < 0.005;
const formatRate = (rate: number) =>
  rate.toLocaleString("de-DE", { maximumFractionDigits: 3 });

export function RefreshRatePicker({
  device,
  item,
  onChange,
}: {
  device: DisplayDevice;
  item: DisplaySetting;
  onChange: (patch: Partial<DisplaySetting>) => void;
}) {
  const portrait = item.rotation === 2 || item.rotation === 4;
  const width = portrait ? item.height : item.width;
  const height = portrait ? item.width : item.height;
  const rates = (device.modes ?? [])
    .filter(
      (mode) =>
        mode.width === width &&
        mode.height === height &&
        Number.isFinite(mode.refreshRate) &&
        mode.refreshRate >= 20 &&
        mode.refreshRate <= 1000,
    )
    .map((mode) => mode.refreshRate)
    .sort((a, b) => b - a)
    .reduce<number[]>((unique, rate) => {
      if (!unique.some((other) => sameRate(other, rate))) unique.push(rate);
      return unique;
    }, []);
  const selected = rates.find((rate) => sameRate(rate, item.refreshRate));
  const [manual, setManual] = useState(false);
  const custom = manual || selected === undefined;
  const current = device.current;

  return (
    <div className="stack gap-2">
      <select
        aria-label={`${device.name} Hz`}
        value={custom ? "custom" : String(selected)}
        onChange={(event) => {
          const value = event.target.value;
          setManual(value === "custom");
          if (value !== "custom") onChange({ refreshRate: Number(value) });
        }}
      >
        {rates.map((rate) => (
          <option key={rate} value={rate}>
            {formatRate(rate)} Hz
            {current &&
            current.width === item.width &&
            current.height === item.height &&
            sameRate(current.refreshRate, rate)
              ? " (aktuell)"
              : ""}
          </option>
        ))}
        <option value="custom">Benutzerdefiniert …</option>
      </select>
      {custom && (
        <input
          type="number"
          aria-label={`${device.name} Hz manuell`}
          min="20"
          max="1000"
          step="0.001"
          value={item.refreshRate}
          onChange={(event) =>
            onChange({ refreshRate: Number(event.target.value) })
          }
        />
      )}
      {rates.length === 0 && (
        <span className="helper">
          Für diese Auflösung wurden keine Hz-Werte gemeldet. Der eingetragene
          Wert bleibt erhalten.
        </span>
      )}
    </div>
  );
}
