import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import {
  Monitor,
  Volume2,
  Settings2,
  LayoutDashboard,
  Plus,
  ArrowUpRight,
  Keyboard,
  RefreshCw,
  Check,
  MoreHorizontal,
  Copy,
  Trash2,
  ShieldCheck,
  Headphones,
  Mic,
  Download,
  Upload,
  Moon,
  Sun,
  ArrowRightLeft,
  Radio,
  CheckCircle2,
  Star,
  SlidersHorizontal,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Switch } from "@/components/ui/switch";
import { ResolutionPicker } from "@/components/ResolutionPicker";
import { RefreshRatePicker } from "@/components/RefreshRatePicker";
import { LiveAlignment } from "@/components/LiveAlignment";
import features from "../../features.json";
import { AudioMixer } from "@/components/AudioMixer";
import { QuickMixer } from "@/components/QuickMixer";
import { BluetoothAdapters } from "@/components/BluetoothAdapters";
import { UpdateSettings } from "@/components/UpdateSettings";
import {
  HomeAssistantSettings,
  type HomeAssistantDraft,
} from "@/components/HomeAssistantSettings";
import { Toaster, toast } from "sonner";
import {
  call,
  appVersion,
  subscribe,
  profileAudio,
  profileAudioName,
  type State,
  type Settings,
  type Profile,
  type Favorite,
  type DisplaySetting,
  type AudioDevice,
  type Endpoint,
} from "@/lib/bridge";

const uid = () => crypto.randomUUID();
function SaveError({
  message,
  title = "Speichern nicht möglich",
}: {
  message: string | null;
  title?: string;
}) {
  const element = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (message) element.current?.scrollIntoView({ block: "nearest" });
  }, [message]);
  return message ? (
    <div
      ref={element}
      role="alert"
      className="rounded-lg border border-red-500/40 bg-red-500/10 p-3 text-sm select-text"
    >
      <strong>{title}</strong>
      <div className="mt-1 whitespace-pre-wrap break-words">{message}</div>
    </div>
  ) : null;
}
function Field({
  label,
  children,
  help,
}: {
  label: string;
  children: ReactNode;
  help?: string;
}) {
  return (
    <div className="field">
      <label>{label}</label>
      {children}
      {help && <span className="helper">{help}</span>}
    </div>
  );
}
function KeyInput({
  value,
  onChange,
}: {
  value: string | null;
  onChange: (value: string | null) => void;
}) {
  const [captureId] = useState(uid);
  useEffect(
    () =>
      subscribe((message) => {
        if (message.event !== "shortcutCaptured") return;
        const data = message.data as { captureId: string; shortcut: string };
        if (data.captureId === captureId) onChange(data.shortcut);
      }),
    [captureId, onChange],
  );
  useEffect(
    () => () => {
      if (window.chrome?.webview)
        void call("releaseShortcut", captureId).catch(() => {});
    },
    [],
  );
  return (
    <div className="flex gap-2">
      <input
        aria-label="Tastenkombination"
        placeholder="Hier klicken und Kombination drücken"
        value={value || ""}
        onFocus={() => {
          if (window.chrome?.webview)
            void call("captureShortcut", captureId).catch((e) =>
              toast.error(e.message),
            );
        }}
        onBlur={() => {
          if (window.chrome?.webview)
            void call("releaseShortcut", captureId).catch((e) =>
              toast.error(e.message),
            );
        }}
        onChange={(e) => onChange(e.target.value || null)}
        onKeyDown={(e) => {
          if (e.key === "Tab") return;
          e.preventDefault();
          if (e.key === "Backspace" || e.key === "Delete") {
            onChange(null);
            return;
          }
          if (!e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey) return;
          const key = /^Numpad[0-9]$/.test(e.code)
            ? e.code.replace("Numpad", "Num")
            : /^(Digit[0-9]|Key[A-Z])$/.test(e.code)
              ? e.code.replace(/^(Digit|Key)/, "")
              : e.key.toUpperCase();
          if (
            /^[A-Z0-9]$/.test(key) ||
            /^Num[0-9]$/.test(key) ||
            /^F\d+$/.test(key)
          )
            onChange(
              [
                e.ctrlKey ? "Ctrl" : null,
                e.altKey ? "Alt" : null,
                e.shiftKey ? "Shift" : null,
                e.metaKey ? "Win" : null,
                key,
              ]
                .filter(Boolean)
                .join("+"),
            );
        }}
      />
      <Button
        variant="ghost"
        size="icon"
        title="Shortcut entfernen"
        onClick={() => onChange(null)}
      >
        ×
      </Button>
    </div>
  );
}
function DeviceSelect({
  devices,
  value,
  onChange,
  input,
  label,
}: {
  devices: AudioDevice[];
  value: string | null;
  onChange: (value: string | null) => void;
  input: boolean;
  label: string;
}) {
  return (
    <select
      aria-label={label}
      value={value || ""}
      onChange={(e) => onChange(e.target.value || null)}
    >
      <option value="">Unverändert lassen</option>
      {value && !devices.some((d) => d.id === value) && (
        <option value={value}>Gespeichertes Gerät fehlt</option>
      )}
      {devices
        .filter((d) => d.input === input)
        .map((d) => (
          <option key={d.id} value={d.id}>
            {d.name}
            {!d.available ? " · nicht verfügbar" : ""}
          </option>
        ))}
    </select>
  );
}
function MonitorPreview({
  monitors,
  editable = false,
  onChange,
}: {
  monitors: DisplaySetting[];
  editable?: boolean;
  onChange?: (d: DisplaySetting[]) => void;
}) {
  if (!monitors.length)
    return (
      <div className="monitor-canvas flex items-center justify-center text-muted-foreground">
        Keine aktiven Monitore
      </div>
    );
  const minX = Math.min(...monitors.map((d) => d.x)),
    minY = Math.min(...monitors.map((d) => d.y));
  const width = Math.max(...monitors.map((d) => d.x + d.width)) - minX,
    height = Math.max(...monitors.map((d) => d.y + d.height)) - minY;
  const factor = Math.min(420 / width, 145 / height);
  return (
    <div className="monitor-canvas">
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: "50%",
          width: width * factor,
          height: height * factor,
          transform: "translate(-50%,-50%)",
        }}
      >
        {monitors.map((d, i) => (
          <div
            key={d.deviceId}
            className={`monitor ${d.primary ? "primary" : ""}`}
            style={{
              left: (d.x - minX) * factor,
              top: (d.y - minY) * factor,
              width: d.width * factor - 3,
              height: d.height * factor - 3,
              cursor: editable ? "grab" : undefined,
            }}
            onPointerDown={(e) => {
              if (!editable || !onChange) return;
              e.currentTarget.setPointerCapture(e.pointerId);
              const startX = e.clientX,
                startY = e.clientY;
              const el = e.currentTarget;
              let next = monitors;
              el.onpointermove = (move) => {
                let x = d.x + Math.round((move.clientX - startX) / factor),
                  y = d.y + Math.round((move.clientY - startY) / factor);
                for (const other of monitors.filter(
                  (o) => o.deviceId !== d.deviceId,
                )) {
                  for (const edge of [other.x + other.width, other.x - d.width])
                    if (Math.abs(x - edge) < 100) x = edge;
                  for (const edge of [
                    other.y,
                    other.y + other.height - d.height,
                  ])
                    if (Math.abs(y - edge) < 100) y = edge;
                }
                next = monitors.map((o) =>
                  o.deviceId === d.deviceId ? { ...o, x, y } : o,
                );
                onChange(next);
              };
              el.onpointerup = () => {
                el.onpointermove = null;
                el.onpointerup = null;
                if (d.primary) {
                  const p = next.find((o) => o.deviceId === d.deviceId)!;
                  onChange(
                    next.map((o) => ({ ...o, x: o.x - p.x, y: o.y - p.y })),
                  );
                }
              };
            }}
          >
            <strong>
              {i + 1}
              {d.primary ? " ★" : ""}
            </strong>
            <span>{d.name}</span>
            <span>
              {d.width} × {d.height}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
export default function App() {
  const [state, setState] = useState<State | null>(null),
    [page, setPage] = useState("workspace"),
    [profile, setProfile] = useState<Profile | null>(null),
    [favorite, setFavorite] = useState<Favorite | null>(null),
    [preferences, setPreferences] = useState<Settings["preferences"] | null>(
      null,
    ),
    [working, setWorking] = useState(false),
    [switching, setSwitching] = useState(false),
    [saveError, setSaveError] = useState<string | null>(null),
    [alignmentError, setAlignmentError] = useState<string | null>(null),
    [quickMixer, setQuickMixer] = useState(false),
    [seconds, setSeconds] = useState(90),
    [licenses, setLicenses] = useState<string | null>(null);
  const audioSummaryTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [homeAssistantDraft, setHomeAssistantDraft] =
    useState<HomeAssistantDraft | null>(null);
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [page]);
  useEffect(
    () => () => {
      if (audioSummaryTimer.current) clearTimeout(audioSummaryTimer.current);
    },
    [],
  );
  async function mixerChanged() {
    if (audioSummaryTimer.current) clearTimeout(audioSummaryTimer.current);
    audioSummaryTimer.current = setTimeout(() => {
      audioSummaryTimer.current = null;
      void call<State>("state")
        .then(setState)
        .catch((error) => toast.error(error.message));
    }, 300);
  }
  useEffect(() => {
    setSaveError(null);
    setAlignmentError(null);
  }, [profile?.id, favorite?.id, !!preferences]);
  useEffect(() => {
    const navigate = (target: unknown) => {
      if (target === "quickMixer") setQuickMixer(true);
      else if (target === "mainWindow") setQuickMixer(false);
      else if (
        target === "audio" ||
        target === "mixer" ||
        target === "settings" ||
        target === "preferences"
      ) {
        setQuickMixer(false);
        setPage(target === "preferences" ? "settings" : target);
      }
    };
    call<State>("state")
      .then((initial) => {
        setState(initial);
        navigate(initial.requestedView);
      })
      .catch((e) => toast.error(e.message));
    return subscribe((m) => {
      if (m.event === "state") setState(m.data as State);
      if (m.event === "windowVisibility")
        setState((current) =>
          current ? { ...current, visible: Boolean(m.data) } : current,
        );
      if (m.event === "error") toast.error(m.error);
      if (m.event === "busy") {
        setWorking(Boolean(m.data));
        setSwitching(Boolean(m.data) && m.scope !== "mixer");
      }
      if (m.event === "navigate") navigate(m.data);
    });
  }, []);
  useEffect(() => {
    if (state && !state.busy) setWorking(false);
  }, [state]);
  useEffect(() => {
    const theme = state?.settings.preferences.theme || "system",
      mq = matchMedia("(prefers-color-scheme: dark)");
    const apply = () => {
      const dark = theme === "dark" || (theme === "system" && mq.matches);
      document.documentElement.classList.toggle("dark", dark);
      if (window.chrome?.webview)
        void call("windowTheme", dark).catch((e) => toast.error(e.message));
    };
    apply();
    mq.addEventListener("change", apply);
    return () => mq.removeEventListener("change", apply);
  }, [state?.settings.preferences.theme]);
  useEffect(() => {
    const tick = () =>
      setSeconds(
        state?.pending
          ? Math.max(
              0,
              Math.ceil(
                (Date.parse(state.pending.deadline) - Date.now()) / 1000,
              ),
            )
          : 90,
      );
    tick();
    const t = setInterval(tick, 1000);
    return () => clearInterval(t);
  }, [state?.pending]);
  async function action(method: string, payload: unknown = null) {
    setWorking(true);
    try {
      await call(method, payload);
      setState(await call<State>("state"));
      return true;
    } catch (e) {
      if (method === "save") setSaveError((e as Error).message);
      else toast.error((e as Error).message);
      return false;
    } finally {
      setWorking(false);
    }
  }
  async function save(s: Settings) {
    setSaveError(null);
    const ok = await action("save", s);
    if (ok) toast.success("Gespeichert");
    return ok;
  }
  if (!state)
    return (
      <div className="h-screen grid place-items-center">
        <div className="stack items-center">
          <RefreshCw className="animate-spin" />
          <p>SetupPulse wird geladen …</p>
          <Button variant="outline" onClick={() => location.reload()}>
            Erneut laden
          </Button>
        </div>
        <Toaster />
      </div>
    );
  const { settings, inventory } = state;
  const disabled = working || state.busy || !!state.pending;
  const applyingMonitor = working || state.busy || !!state.pending?.applying;
  const current = inventory.displays
    .filter((d) => d.active)
    .map((d) => d.current!);
  const output = inventory.audio.find((d) => !d.input && d.default),
    input = inventory.audio.find((d) => d.input && d.default);
  const selected = settings.profiles.find((p) => p.id === state.activeProfile);
  const linked = profileAudio(selected, settings);
  const mismatch =
    linked &&
    ([linked.output, linked.input].some(
      (s) =>
        s &&
        !inventory.audio.some(
          (d) =>
            d.id === s.deviceId &&
            d.available &&
            d.default &&
            (s.volume === null || Math.abs(d.volume - s.volume) <= 1) &&
            (s.muted === null || s.muted === d.muted),
        ),
    ) ||
      [linked.communicationsOutput, linked.communicationsInput].some(
        (id) =>
          id &&
          !inventory.audio.some(
            (d) => d.id === id && d.available && d.communications,
          ),
      ));
  const createProfile = () =>
    setProfile({
      id: uid(),
      name: "Neues Setup",
      monitors: structuredClone(current),
      audioFavoriteId: null,
      shortcut: null,
      verifiedHash: null,
    });
  const createFavorite = () =>
    setFavorite({
      id: uid(),
      name: "Neuer Audiofavorit",
      output: output
        ? { deviceId: output.id, volume: null, muted: null }
        : null,
      input: null,
      communicationsOutput: null,
      communicationsInput: null,
      shortcut: null,
    });
  const favoriteDevice = (device: AudioDevice) =>
    setFavorite({
      id: uid(),
      name: device.name.slice(0, 80),
      output: device.input
        ? null
        : { deviceId: device.id, volume: null, muted: null },
      input: device.input
        ? { deviceId: device.id, volume: null, muted: null }
        : null,
      communicationsOutput: null,
      communicationsInput: null,
      shortcut: null,
    });
  const favoritesForDevice = (device: AudioDevice) =>
    settings.audioFavorites.filter((f) =>
      device.input
        ? f.input?.deviceId === device.id || f.communicationsInput === device.id
        : f.output?.deviceId === device.id ||
          f.communicationsOutput === device.id,
    );
  const changeProfileEndpoint = (
    key: "output" | "input",
    id: string | null,
  ) => {
    if (!profile) return;
    const audio = {
      output: null,
      input: null,
      name: null,
      ...profile.audio,
      [key]: id ? { deviceId: id, volume: null, muted: null } : null,
    };
    setProfile({
      ...profile,
      audioFavoriteId: null,
      audio: audio.output || audio.input ? audio : null,
    });
  };
  const removeProfile = async (p: Profile) => {
    if (
      await save({
        ...settings,
        profiles: settings.profiles.filter((x) => x.id !== p.id),
      })
    )
      setProfile(null);
  };
  const removeFavorite = async (f: Favorite) => {
    if (
      await save({
        ...settings,
        audioFavorites: settings.audioFavorites.filter((x) => x.id !== f.id),
        profiles: settings.profiles.map((p) =>
          p.audioFavoriteId === f.id ? { ...p, audioFavoriteId: null } : p,
        ),
      })
    )
      setFavorite(null);
  };
  const profileCards = (
    <div className="profile-grid">
      {settings.profiles.map((p) => (
        <Card
          key={p.id}
          className={selected?.id === p.id ? "border-primary/70" : ""}
        >
          <CardHeader className="pb-3">
            <div className="row">
              <span className={`pill ${selected?.id === p.id ? "purple" : ""}`}>
                {selected?.id === p.id ? (
                  <Check size={12} />
                ) : (
                  <Monitor size={12} />
                )}{" "}
                {selected?.id === p.id ? "Zuletzt aktiviert" : "Setup"}
              </span>
              <Button
                variant="ghost"
                size="icon"
                title={`${p.name} bearbeiten`}
                onClick={() => setProfile(structuredClone(p))}
              >
                <MoreHorizontal size={18} />
              </Button>
            </div>
            <CardTitle className="text-xl mt-3">{p.name}</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="stack">
              <p className="text-xs">
                {p.monitors
                  ? `${p.monitors.length} Monitore · ${p.monitors.find((d) => d.primary)?.name}`
                  : "Monitore unverändert"}
                <br />
                {profileAudioName(p, settings, inventory.audio)}
              </p>
              <div className="row">
                <span className="shortcut">
                  {p.shortcut || "Kein Shortcut"}
                </span>
                <span className="helper">
                  {p.monitors
                    ? p.verifiedHash
                      ? "Geprüft"
                      : "Test erforderlich"
                    : "Audio-Setup"}
                </span>
              </div>
              <Button
                disabled={disabled}
                className="w-full"
                onClick={() => action("applyProfile", p.id)}
              >
                Aktivieren <ArrowUpRight size={15} />
              </Button>
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
  const audioPanel = (
    <Card>
      <CardHeader>
        <div className="row">
          <CardTitle className="text-base">
            Audio · unabhängig wechseln
          </CardTitle>
          <Headphones size={19} className="text-muted-foreground" />
        </div>
      </CardHeader>
      <CardContent>
        <div className="device-row">
          <div className="device-icon">
            <Volume2 size={18} />
          </div>
          <div className="min-w-0">
            <div className="text-xs text-muted-foreground">
              AKTUELLE AUSGABE
            </div>
            <div className="truncate mt-1 font-medium">
              {output?.name || "Kein Gerät"}
            </div>
            <p className="text-xs">
              {output
                ? `${Math.round(output.volume)}%${output.muted ? " · Stumm" : ""}`
                : ""}
            </p>
          </div>
        </div>
        <div className="device-row">
          <div className="device-icon">
            <Mic size={18} />
          </div>
          <div className="min-w-0">
            <div className="text-xs text-muted-foreground">
              AKTUELLE EINGABE
            </div>
            <div className="truncate mt-1 font-medium">
              {input?.name || "Kein Gerät"}
            </div>
          </div>
        </div>
        {mismatch && (
          <div className="notice mt-3">
            Audio wurde separat geändert. „{selected?.name}“ stellt beim
            Aktivieren „
            {selected && profileAudioName(selected, settings, inventory.audio)}“
            wieder her.
          </div>
        )}
        <div className="stack mt-3">
          {settings.audioFavorites.map((f) => (
            <div className="row" key={f.id}>
              <button
                className="text-left min-w-0"
                onClick={() => setFavorite(structuredClone(f))}
              >
                <span className="block font-medium truncate">{f.name}</span>
                <span className="helper">{f.shortcut || "Kein Shortcut"}</span>
              </button>
              <Button
                variant="outline"
                size="sm"
                disabled={disabled}
                onClick={() => action("applyAudio", f.id)}
              >
                Wechseln
              </Button>
            </div>
          ))}
          <Button
            variant="ghost"
            className="justify-start"
            onClick={createFavorite}
          >
            <Plus size={15} />
            Audiofavorit hinzufügen
          </Button>
        </div>
      </CardContent>
    </Card>
  );
  if (quickMixer)
    return (
      <>
        <QuickMixer
          disabled={
            !!state.pending || switching || (state.busy && !state.busyMixer)
          }
          visible={state.visible ?? true}
          logarithmic={settings.preferences.logarithmicVolume ?? false}
          changed={mixerChanged}
          close={() => {
            if (window.chrome?.webview)
              void call("closeQuickMixer").catch((error) =>
                toast.error(error.message),
              );
            else setQuickMixer(false);
          }}
          openFull={() => {
            if (window.chrome?.webview)
              void call("openFullMixer").catch((error) =>
                toast.error(error.message),
              );
            else {
              setQuickMixer(false);
              setPage("mixer");
            }
          }}
        />
        <Toaster richColors closeButton theme={settings.preferences.theme} />
      </>
    );
  return (
    <div className="app">
      {createPortal(
        <Toaster
          richColors
          closeButton
          theme={settings.preferences.theme}
          position="bottom-right"
        />,
        document.body,
      )}
      <aside className="sidebar">
        <div className="brand">
          <img
            className="brand-mark"
            src="/brand/setuppulse.png"
            alt=""
            width={34}
            height={34}
          />
          SetupPulse
        </div>
        <div className="eyebrow px-3 mb-4">Dein Arbeitsplatz</div>
        {[
          ["workspace", "Workspace", LayoutDashboard],
          ["profiles", "Setups", Monitor],
          ["audio", "Audio", Headphones],
          ["mixer", "Audio-Mixer", SlidersHorizontal],
          ["settings", "Einstellungen", Settings2],
        ].map(([id, label, Icon]) => (
          <button
            key={id as string}
            className={`nav ${page === id ? "active" : ""}`}
            aria-current={page === id ? "page" : undefined}
            onClick={() => setPage(id as string)}
          >
            {typeof Icon !== "string" && <Icon size={18} />} {label as string}
          </button>
        ))}
        <div className="sidebar-footer">
          <div className="flex gap-2 items-center text-xs text-muted-foreground">
            <ShieldCheck size={15} /> Lokal auf deinem PC
          </div>
          <p className="text-xs mt-2">v{appVersion} · Windows 11</p>
        </div>
      </aside>
      <main className="main">
        <div className="topline">
          <span className="eyebrow">
            {page === "workspace"
              ? "Übersicht"
              : page === "profiles"
                ? "Setups"
                : page === "audio"
                  ? "Audiogeräte"
                  : page === "mixer"
                    ? "Audio-Mixer"
                    : "Einstellungen"}
          </span>
          <div className="flex items-center gap-3">
            {state.demo && (
              <span className="pill">Demo · keine Geräteänderungen</span>
            )}
            <span className="pill">
              <Radio size={11} />
              {inventory.displays.filter((d) => d.connected).length} Monitore
              erkannt
            </span>
            <Button
              variant="ghost"
              size="icon"
              title="Geräte aktualisieren"
              disabled={working}
              onClick={() => action("state")}
            >
              <RefreshCw size={16} />
            </Button>
          </div>
        </div>
        {state.warning && <div className="notice mb-4">{state.warning}</div>}
        {(page === "workspace" || page === "profiles") && (
          <>
            <div className="hero">
              <div>
                <h1>
                  {page === "workspace"
                    ? "Dein Setup. Ein Wechsel."
                    : "Deine Setups"}
                </h1>
                <p>Monitore, Sound und Shortcuts – so, wie du arbeitest.</p>
              </div>
              <Button disabled={disabled} onClick={createProfile}>
                <Plus size={16} />
                Setup erstellen
              </Button>
            </div>
            {!settings.profiles.length ? (
              <div className="empty">
                <Monitor className="text-primary" size={34} />
                <h2>Dein erstes Setup beginnt hier</h2>
                <p className="max-w-md mx-auto mt-2 mb-5">
                  Speichere deine aktuelle Monitoranordnung und verknüpfe bei
                  Bedarf einen Audiofavoriten. Weitere Setups richtest du danach
                  ein.
                </p>
                <Button disabled={disabled} onClick={createProfile}>
                  Aktuelle Anordnung übernehmen
                </Button>
              </div>
            ) : (
              profileCards
            )}
          </>
        )}
        {page === "workspace" && (
          <div className="lower-grid">
            <Card>
              <CardHeader>
                <div className="row">
                  <CardTitle className="text-base">
                    Aktuelle Monitoranordnung
                  </CardTitle>
                  <span className="pill">Live</span>
                </div>
              </CardHeader>
              <CardContent>
                <MonitorPreview monitors={current} />
                <div className="row mt-4">
                  <p className="text-xs">
                    {current.length} aktiv · ★ Hauptmonitor
                  </p>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={disabled}
                    onClick={createProfile}
                  >
                    Als Setup speichern <ArrowUpRight size={14} />
                  </Button>
                </div>
              </CardContent>
            </Card>
            {audioPanel}
          </div>
        )}
        {page === "mixer" && (
          <AudioMixer
            disabled={
              !!state.pending || switching || (state.busy && !state.busyMixer)
            }
            visible={state.visible ?? true}
            logarithmic={settings.preferences.logarithmicVolume ?? false}
            changed={mixerChanged}
          />
        )}
        {page === "audio" && (
          <>
            <div className="hero">
              <div>
                <h1>Dein Sound. Dein Shortcut.</h1>
                <p>
                  Headsets und Lautsprecher wechseln, während die Monitore
                  bleiben.
                </p>
              </div>
              <Button disabled={disabled} onClick={createFavorite}>
                <Plus size={16} />
                Audiofavorit
              </Button>
            </div>
            <div className="flex gap-2 flex-wrap mb-6">
              {[
                ["sound", "Windows-Soundeinstellungen"],
                ["mixer", "Windows-Lautstärkemixer"],
                ["playback", "Wiedergabegeräte"],
                ["recording", "Aufnahmegeräte"],
                ["sounds", "Systemsounds"],
              ].map(([target, label]) => (
                <Button
                  key={target}
                  variant="outline"
                  size="sm"
                  onClick={() => action("openAudioSettings", target)}
                >
                  {label}
                </Button>
              ))}
            </div>
            <div className="lower-grid">
              {audioPanel}
              <Card>
                <CardHeader>
                  <CardTitle>Erkannte Audiogeräte</CardTitle>
                </CardHeader>
                <CardContent>
                  {inventory.audio.map((d) => {
                    const savedFavorites = favoritesForDevice(d);
                    return (
                      <div className="device-row" key={`${d.input}-${d.id}`}>
                        {d.input ? <Mic size={17} /> : <Volume2 size={17} />}
                        <div className="flex-1 min-w-0">
                          <div className="truncate" title={d.name}>
                            {d.name}
                          </div>
                          <p className="text-xs">
                            {d.input ? "Eingabe" : "Ausgabe"} ·{" "}
                            {d.available ? "Verfügbar" : "Nicht verfügbar"}
                            {d.communications ? " · Kommunikation" : ""}
                          </p>
                        </div>
                        {d.default && (
                          <span
                            title="Aktuelles Windows-Standardgerät"
                            aria-label="Aktuelles Windows-Standardgerät"
                          >
                            <CheckCircle2 className="text-primary" size={16} />
                          </span>
                        )}
                        {savedFavorites.length ? (
                          <div className="flex flex-col items-end gap-1 min-w-0 max-w-36">
                            {savedFavorites.map((f) => (
                              <Button
                                key={f.id}
                                variant="ghost"
                                className="max-w-full"
                                disabled={disabled}
                                aria-label={`Audiofavorit ${f.name} bearbeiten`}
                                title={`Favorit bearbeiten: ${f.name}`}
                                onClick={() => setFavorite(structuredClone(f))}
                              >
                                <Star
                                  size={14}
                                  className="shrink-0 text-primary fill-primary"
                                />
                                <span className="truncate">{f.name}</span>
                              </Button>
                            ))}
                          </div>
                        ) : (
                          <Button
                            variant="ghost"
                            disabled={disabled}
                            aria-label={`${d.name} favorisieren`}
                            onClick={() => favoriteDevice(d)}
                          >
                            Favorisieren
                          </Button>
                        )}
                      </div>
                    );
                  })}
                </CardContent>
              </Card>
            </div>
          </>
        )}
        {page === "settings" && (
          <>
            <div className="hero">
              <div>
                <h1>Alles an seinem Platz.</h1>
                <p>Darstellung, Shortcuts und Verhalten beim Start.</p>
              </div>
            </div>
            <div className="lower-grid">
              <Card>
                <CardHeader>
                  <CardTitle>Allgemein & Shortcuts</CardTitle>
                </CardHeader>
                <CardContent className="stack">
                  <div className="row">
                    <div>
                      <strong>Deine Einstellungen</strong>
                      <p className="text-xs">
                        Windows-Start und Startverhalten festlegen.
                      </p>
                    </div>
                    <Button
                      variant="outline"
                      disabled={disabled}
                      onClick={() =>
                        setPreferences({ ...settings.preferences })
                      }
                    >
                      Bearbeiten
                    </Button>
                  </div>
                  <div className="device-row">
                    <Moon size={17} />
                    <span>Design: {settings.preferences.theme}</span>
                  </div>
                  <div className="device-row">
                    <Keyboard size={17} />
                    <span>
                      Rettungs-Shortcut:{" "}
                      {settings.preferences.rescueShortcut || "Nicht vergeben"}
                    </span>
                  </div>
                  <div className="row">
                    <Button
                      variant="outline"
                      disabled={disabled}
                      onClick={() => action("export")}
                    >
                      <Download size={16} />
                      Exportieren
                    </Button>
                    <Button
                      variant="outline"
                      disabled={disabled}
                      onClick={() => action("import")}
                    >
                      <Upload size={16} />
                      Importieren
                    </Button>
                  </div>
                  <Button
                    variant="ghost"
                    onClick={() =>
                      call<string>("licenses")
                        .then(setLicenses)
                        .catch((e) => toast.error(e.message))
                    }
                  >
                    Über SetupPulse · Lizenzen
                  </Button>
                  <p className="helper">
                    Import fügt Profile hinzu. Gerätebindungen müssen auf einem
                    anderen PC neu zugeordnet werden. Monitortests und Shortcuts
                    werden nicht übernommen.
                  </p>
                  <Button
                    variant="outline"
                    disabled={disabled}
                    onClick={() => action("rescue")}
                  >
                    <ShieldCheck size={16} />
                    Sichere Konfiguration wiederherstellen
                  </Button>
                </CardContent>
              </Card>
              <UpdateSettings disabled={disabled} />
              <BluetoothAdapters />
              <HomeAssistantSettings
                disabled={disabled}
                draft={homeAssistantDraft}
                setDraft={setHomeAssistantDraft}
              />
            </div>
          </>
        )}
        <p className="helper mt-7 flex gap-2 items-center">
          <ShieldCheck size={14} /> Neue Monitoranordnungen werden 90 Sekunden
          getestet und ohne Bestätigung zurückgesetzt.
        </p>
      </main>
      <Dialog
        open={licenses !== null}
        onOpenChange={(open) => !open && setLicenses(null)}
      >
        <DialogContent className="sm:max-w-[800px] max-h-[85vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>SetupPulse · Lizenzen</DialogTitle>
            <DialogDescription>
              Eigener Code und verwendete Komponenten.
            </DialogDescription>
          </DialogHeader>
          <pre className="text-xs whitespace-pre-wrap break-words">
            {licenses}
          </pre>
        </DialogContent>
      </Dialog>
      <Dialog
        open={!!profile && !state.pending?.liveAlignment && !quickMixer}
        onOpenChange={(open) => !open && setProfile(null)}
      >
        <DialogContent className="sm:max-w-[850px] max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>Setup bearbeiten</DialogTitle>
            <DialogDescription>
              Monitore und Audio lassen sich gemeinsam oder einzeln festlegen.
            </DialogDescription>
          </DialogHeader>
          {profile && (
            <div className="stack">
              <div className="field-grid">
                <Field label="Name">
                  <input
                    aria-label="Setupname"
                    maxLength={80}
                    value={profile.name}
                    onChange={(e) =>
                      setProfile({ ...profile, name: e.target.value })
                    }
                  />
                </Field>
                <Field label="Globaler Shortcut">
                  <KeyInput
                    value={profile.shortcut}
                    onChange={(shortcut) =>
                      setProfile({ ...profile, shortcut })
                    }
                  />
                </Field>
              </div>
              <div className="row">
                <label>Monitoranordnung anwenden</label>
                <Switch
                  aria-label="Monitoranordnung anwenden"
                  checked={profile.monitors !== null}
                  onCheckedChange={(on) =>
                    setProfile({
                      ...profile,
                      monitors: on ? structuredClone(current) : null,
                    })
                  }
                />
              </div>
              {profile.monitors !== null && (
                <>
                  <MonitorPreview
                    monitors={profile.monitors}
                    editable
                    onChange={(monitors) =>
                      setProfile((p) => (p ? { ...p, monitors } : null))
                    }
                  />
                  {features.liveMonitorAlignment && (
                    <div className="stack gap-2">
                      <Button
                        className="self-start"
                        variant="outline"
                        disabled={disabled || current.length < 2}
                        onClick={() => {
                          setAlignmentError(null);
                          void action("beginAlignment");
                        }}
                      >
                        Monitorhöhe live abgleichen
                      </Button>
                      <p className="helper">
                        Verschiebt die in Windows aktiven Monitore dieses
                        Entwurfs. Andere aktive Displays bleiben unverändert.
                        Bestätigte Höhen werden in diesen Entwurf übernommen;
                        anschließend das Setup speichern.
                      </p>
                    </div>
                  )}
                  <p className="helper">
                    Ziehen oder Positionen eingeben. Anordnungen müssen an einer
                    Kante verbunden sein. Hauptmonitor: 0 / 0.
                  </p>
                  {inventory.displays.map((d) => {
                    const item = profile.monitors!.find(
                      (m) => m.deviceId === d.id,
                    );
                    const suggested =
                      d.modes?.find((m) => m.recommended) || d.modes?.[0];
                    const update = (patch: Partial<DisplaySetting>) =>
                      setProfile({
                        ...profile,
                        monitors: profile.monitors!.map((m) =>
                          m.deviceId === d.id ? { ...m, ...patch } : m,
                        ),
                      });
                    return (
                      <div className="monitor-editor" key={d.id}>
                        <div className="row">
                          <strong>{d.name}</strong>
                          <div className="flex items-center gap-3">
                            <label className="text-xs">
                              {item
                                ? "Im Setup aktiv"
                                : "Im Setup ausgeschaltet"}
                            </label>
                            <Switch
                              aria-label={`${d.name} aktiv`}
                              checked={!!item}
                              onCheckedChange={(on) =>
                                setProfile({
                                  ...profile,
                                  monitors: on
                                    ? [
                                        ...profile.monitors!,
                                        d.current || {
                                          deviceId: d.id,
                                          name: d.name,
                                          width: suggested?.width || 1920,
                                          height: suggested?.height || 1080,
                                          x: profile.monitors!.reduce(
                                            (max, m) =>
                                              Math.max(max, m.x + m.width),
                                            0,
                                          ),
                                          y: 0,
                                          rotation: 1,
                                          refreshRate:
                                            suggested?.refreshRate || 60,
                                          scale: 100,
                                          primary:
                                            profile.monitors!.length === 0,
                                        },
                                      ]
                                    : profile.monitors!.filter(
                                        (m) => m.deviceId !== d.id,
                                      ),
                                })
                              }
                            />
                          </div>
                        </div>
                        {item && (
                          <>
                            <ResolutionPicker
                              device={d}
                              item={item}
                              onChange={update}
                            />
                            <div className="monitor-fields">
                              {(["x", "y"] as const).map((k, i) => (
                                <Field key={k} label={["X", "Y"][i]}>
                                  <input
                                    type="number"
                                    aria-label={`${d.name} ${k}`}
                                    value={item[k]}
                                    onChange={(e) =>
                                      update({ [k]: Number(e.target.value) })
                                    }
                                  />
                                </Field>
                              ))}
                              <Field label="Hz">
                                <RefreshRatePicker
                                  device={d}
                                  item={item}
                                  onChange={update}
                                />
                              </Field>
                              <Field label="Skalierung">
                                <select
                                  value={item.scale}
                                  onChange={(e) =>
                                    update({ scale: Number(e.target.value) })
                                  }
                                >
                                  {[
                                    100, 125, 150, 175, 200, 225, 250, 300, 350,
                                    400, 450, 500,
                                  ].map((n) => (
                                    <option key={n} value={n}>
                                      {n}%
                                    </option>
                                  ))}
                                </select>
                              </Field>
                              <Field label="Ausrichtung">
                                <select
                                  aria-label={`${d.name} Ausrichtung`}
                                  value={item.rotation}
                                  onChange={(e) =>
                                    update({
                                      rotation: Number(e.target.value),
                                      width:
                                        Number(e.target.value) % 2 !==
                                        item.rotation % 2
                                          ? item.height
                                          : item.width,
                                      height:
                                        Number(e.target.value) % 2 !==
                                        item.rotation % 2
                                          ? item.width
                                          : item.height,
                                    })
                                  }
                                >
                                  {[
                                    [1, "Quer"],
                                    [2, "Hoch"],
                                    [3, "Quer gedreht"],
                                    [4, "Hoch gedreht"],
                                  ].map(([v, t]) => (
                                    <option key={v} value={v}>
                                      {t}
                                    </option>
                                  ))}
                                </select>
                              </Field>
                              <Field label="Hauptmonitor">
                                <Button
                                  variant={item.primary ? "default" : "outline"}
                                  onClick={() =>
                                    setProfile({
                                      ...profile,
                                      monitors: profile.monitors!.map((m) => ({
                                        ...m,
                                        primary: m.deviceId === d.id,
                                        x: m.x - item.x,
                                        y: m.y - item.y,
                                      })),
                                    })
                                  }
                                >
                                  {item.primary ? "Hauptmonitor" : "Festlegen"}
                                </Button>
                              </Field>
                            </div>
                            <Field label="Gerätebindung">
                              <select
                                aria-label="Monitor neu zuordnen"
                                value={item.deviceId}
                                onChange={(e) => {
                                  const device = inventory.displays.find(
                                    (x) => x.id === e.target.value,
                                  )!;
                                  setProfile({
                                    ...profile,
                                    monitors: profile.monitors!.map((m) =>
                                      m.deviceId === d.id
                                        ? {
                                            ...m,
                                            deviceId: device.id,
                                            name: device.name,
                                          }
                                        : m,
                                    ),
                                  });
                                }}
                              >
                                {inventory.displays.map((device) => (
                                  <option key={device.id} value={device.id}>
                                    {device.name}
                                  </option>
                                ))}
                              </select>
                            </Field>
                          </>
                        )}
                      </div>
                    );
                  })}
                  {profile.monitors
                    .filter(
                      (m) =>
                        !inventory.displays.some((d) => d.id === m.deviceId),
                    )
                    .map((m) => (
                      <div className="notice" key={m.deviceId}>
                        <div>Gespeicherter Monitor fehlt: {m.name}</div>
                        <select
                          className="mt-2"
                          value=""
                          onChange={(e) => {
                            const d = inventory.displays.find(
                              (d) => d.id === e.target.value,
                            )!;
                            setProfile({
                              ...profile,
                              monitors: profile.monitors!.map((x) =>
                                x.deviceId === m.deviceId
                                  ? { ...x, deviceId: d.id, name: d.name }
                                  : x,
                              ),
                            });
                          }}
                        >
                          <option value="">Gerät zuordnen …</option>
                          {inventory.displays.map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name}
                            </option>
                          ))}
                        </select>
                      </div>
                    ))}
                </>
              )}
              <Field
                label="Audio im Setup"
                help="Geräte direkt auswählen oder einen gespeicherten Favoriten verwenden. Audio wird erst beim Aktivieren des Setups gewechselt."
              >
                <select
                  aria-label="Audio im Setup"
                  value={profile.audioFavoriteId || ""}
                  onChange={(e) =>
                    setProfile({
                      ...profile,
                      audioFavoriteId: e.target.value || null,
                      audio: null,
                    })
                  }
                >
                  <option value="">Geräte direkt auswählen</option>
                  <optgroup label="Audiofavoriten">
                    {settings.audioFavorites.map((f) => (
                      <option key={f.id} value={f.id}>
                        {f.name}
                      </option>
                    ))}
                  </optgroup>
                </select>
              </Field>
              {!profile.audioFavoriteId && (
                <>
                  <div className="field-grid">
                    <Field label="Ausgabegerät">
                      <DeviceSelect
                        devices={inventory.audio}
                        input={false}
                        label="Setup-Ausgabe"
                        value={profile.audio?.output?.deviceId || null}
                        onChange={(id) => changeProfileEndpoint("output", id)}
                      />
                    </Field>
                    <Field label="Mikrofon">
                      <DeviceSelect
                        devices={inventory.audio}
                        input={true}
                        label="Setup-Mikrofon"
                        value={profile.audio?.input?.deviceId || null}
                        onChange={(id) => changeProfileEndpoint("input", id)}
                      />
                    </Field>
                  </div>
                  {profile.audio ? (
                    <>
                      <Field
                        label="Audio-Anzeigename (optional)"
                        help="Nur in diesem Setup. Der Windows-Gerätename bleibt erhalten."
                      >
                        <input
                          aria-label="Audio-Anzeigename"
                          maxLength={80}
                          placeholder="Gerätenamen verwenden"
                          value={profile.audio.name || ""}
                          onChange={(e) =>
                            setProfile({
                              ...profile,
                              audio: {
                                ...profile.audio!,
                                name: e.target.value || null,
                              },
                            })
                          }
                        />
                      </Field>
                      <Button
                        variant="outline"
                        className="self-start"
                        onClick={() =>
                          setFavorite({
                            id: uid(),
                            name: profileAudioName(
                              profile,
                              settings,
                              inventory.audio,
                            ).slice(0, 80),
                            output: profile.audio!.output,
                            input: profile.audio!.input,
                            communicationsOutput: null,
                            communicationsInput: null,
                            shortcut: null,
                          })
                        }
                      >
                        Als Audiofavorit speichern
                      </Button>
                    </>
                  ) : (
                    <p className="helper">
                      Audio bleibt unverändert. Ein Favorit ist für die
                      Geräteauswahl nicht erforderlich.
                    </p>
                  )}
                </>
              )}
              <SaveError
                message={alignmentError || saveError}
                title={alignmentError ? "Live-Abgleich abgebrochen" : undefined}
              />
              <DialogFooter className="sm:justify-between">
                <div className="flex gap-2">
                  {settings.profiles.some((p) => p.id === profile.id) && (
                    <>
                      <Button
                        variant="ghost"
                        title="Setup löschen"
                        onClick={() => removeProfile(profile)}
                      >
                        <Trash2 size={16} />
                      </Button>
                      <Button
                        variant="ghost"
                        title="Setup duplizieren"
                        onClick={() =>
                          setProfile({
                            ...profile,
                            id: uid(),
                            name: profile.name + " Kopie",
                            shortcut: null,
                            verifiedHash: null,
                          })
                        }
                      >
                        <Copy size={16} />
                      </Button>
                    </>
                  )}
                </div>
                <Button
                  disabled={working}
                  onClick={async () => {
                    if (
                      await save({
                        ...settings,
                        profiles: [
                          ...settings.profiles.filter(
                            (p) => p.id !== profile.id,
                          ),
                          profile,
                        ],
                      })
                    )
                      setProfile(null);
                  }}
                >
                  Setup speichern
                </Button>
              </DialogFooter>
            </div>
          )}
        </DialogContent>
      </Dialog>
      <Dialog
        open={!!favorite && !quickMixer}
        onOpenChange={(open) => !open && setFavorite(null)}
      >
        <DialogContent className="sm:max-w-[660px] max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>Audiofavorit bearbeiten</DialogTitle>
            <DialogDescription>
              Eigener Shortcut – ohne die Monitoranordnung zu ändern.
            </DialogDescription>
          </DialogHeader>
          {favorite && (
            <div className="stack">
              <div className="field-grid">
                <Field label="Name">
                  <input
                    aria-label="Favoritenname"
                    value={favorite.name}
                    maxLength={80}
                    onChange={(e) =>
                      setFavorite({ ...favorite, name: e.target.value })
                    }
                  />
                </Field>
                <Field label="Shortcut">
                  <KeyInput
                    value={favorite.shortcut}
                    onChange={(shortcut) =>
                      setFavorite({ ...favorite, shortcut })
                    }
                  />
                </Field>
              </div>
              {(["output", "input"] as const).map((key, i) => {
                const endpoint = favorite[key];
                const change = (patch: Partial<Endpoint>) =>
                  setFavorite({
                    ...favorite,
                    [key]: { ...endpoint, ...patch },
                  });
                return (
                  <div className="monitor-editor stack" key={key}>
                    <Field label={i ? "Standard-Eingabe" : "Standard-Ausgabe"}>
                      <DeviceSelect
                        devices={inventory.audio}
                        input={!!i}
                        value={endpoint?.deviceId || null}
                        label={i ? "Standard-Eingabe" : "Standard-Ausgabe"}
                        onChange={(id) =>
                          setFavorite({
                            ...favorite,
                            [key]: id
                              ? { deviceId: id, volume: null, muted: null }
                              : null,
                          })
                        }
                      />
                    </Field>
                    {endpoint && (
                      <>
                        <div className="row">
                          <label>Lautstärke festlegen</label>
                          <Switch
                            aria-label={`${key} Lautstärke festlegen`}
                            checked={endpoint.volume !== null}
                            onCheckedChange={(on) =>
                              change({ volume: on ? 50 : null })
                            }
                          />
                        </div>
                        {endpoint.volume !== null && (
                          <div className="flex gap-3 items-center">
                            <input
                              aria-label={`${key} Lautstärke`}
                              type="range"
                              min="0"
                              max="100"
                              value={endpoint.volume}
                              onChange={(e) =>
                                change({ volume: Number(e.target.value) })
                              }
                            />
                            <span>{endpoint.volume}%</span>
                          </div>
                        )}
                        <Field label="Stummschaltung">
                          <select
                            value={
                              endpoint.muted === null
                                ? "keep"
                                : String(endpoint.muted)
                            }
                            onChange={(e) =>
                              change({
                                muted:
                                  e.target.value === "keep"
                                    ? null
                                    : e.target.value === "true",
                              })
                            }
                          >
                            <option value="keep">Unverändert</option>
                            <option value="false">Ton an</option>
                            <option value="true">Stumm</option>
                          </select>
                        </Field>
                      </>
                    )}
                  </div>
                );
              })}
              <div className="field-grid">
                <Field label="Kommunikation · Ausgabe">
                  <DeviceSelect
                    devices={inventory.audio}
                    input={false}
                    label="Kommunikation Ausgabe"
                    value={favorite.communicationsOutput}
                    onChange={(id) =>
                      setFavorite({ ...favorite, communicationsOutput: id })
                    }
                  />
                </Field>
                <Field label="Kommunikation · Eingabe">
                  <DeviceSelect
                    devices={inventory.audio}
                    input
                    label="Kommunikation Eingabe"
                    value={favorite.communicationsInput}
                    onChange={(id) =>
                      setFavorite({ ...favorite, communicationsInput: id })
                    }
                  />
                </Field>
              </div>
              <SaveError message={saveError} />
              <DialogFooter className="sm:justify-between">
                <div>
                  {settings.audioFavorites.some(
                    (f) => f.id === favorite.id,
                  ) && (
                    <Button
                      variant="ghost"
                      title="Audiofavorit löschen"
                      onClick={() => removeFavorite(favorite)}
                    >
                      <Trash2 size={16} />
                    </Button>
                  )}
                </div>
                <Button
                  disabled={working}
                  onClick={async () => {
                    if (
                      await save({
                        ...settings,
                        audioFavorites: [
                          ...settings.audioFavorites.filter(
                            (f) => f.id !== favorite.id,
                          ),
                          favorite,
                        ],
                      })
                    ) {
                      setFavorite(null);
                    }
                  }}
                >
                  Audiofavorit speichern
                </Button>
              </DialogFooter>
            </div>
          )}
        </DialogContent>
      </Dialog>
      <Dialog
        open={!!preferences && !quickMixer}
        onOpenChange={(open) => !open && setPreferences(null)}
      >
        <DialogContent className="max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>Einstellungen</DialogTitle>
            <DialogDescription>
              Globale Shortcuts funktionieren auch bei geschlossenem Fenster.
            </DialogDescription>
          </DialogHeader>
          {preferences && (
            <div className="stack">
              <Field label="Design">
                <select
                  value={preferences.theme}
                  onChange={(e) =>
                    setPreferences({
                      ...preferences,
                      theme: e.target.value as "system" | "dark" | "light",
                    })
                  }
                >
                  <option value="system">System</option>
                  <option value="dark">Dunkel</option>
                  <option value="light">Hell</option>
                </select>
              </Field>
              <div className="row">
                <label>Mit Windows starten</label>
                <Switch
                  aria-label="Autostart"
                  checked={preferences.startWithWindows}
                  onCheckedChange={(on) =>
                    setPreferences({ ...preferences, startWithWindows: on })
                  }
                />
              </div>
              <div className="row gap-3">
                <label>Beim Windows-Start minimiert starten</label>
                <Switch
                  aria-label="Minimierter Windows-Start"
                  disabled={!preferences.startWithWindows}
                  checked={preferences.startMinimized ?? true}
                  onCheckedChange={(on) =>
                    setPreferences({ ...preferences, startMinimized: on })
                  }
                />
              </div>
              <p className="helper">
                Bei aktivierter Option läuft SetupPulse nur im Infobereich neben
                der Uhr. Beim manuellen Öffnen erscheint das Hauptfenster.
              </p>
              <div className="row">
                <label>Beim Schließen im Tray bleiben</label>
                <Switch
                  checked={preferences.closeToTray}
                  onCheckedChange={(on) =>
                    setPreferences({ ...preferences, closeToTray: on })
                  }
                />
              </div>
              <div className="row gap-3">
                <label>Mausrad am Tray-Symbol regelt die Lautstärke</label>
                <Switch
                  aria-label="Tray-Mausrad"
                  checked={preferences.trayScrollVolume ?? false}
                  onCheckedChange={(on) =>
                    setPreferences({ ...preferences, trayScrollVolume: on })
                  }
                />
              </div>
              <div className="row gap-3">
                <label>Feinere Lautstärkeregelung im leisen Bereich</label>
                <Switch
                  aria-label="Logarithmische Lautstärke"
                  checked={preferences.logarithmicVolume ?? false}
                  onCheckedChange={(on) =>
                    setPreferences({ ...preferences, logarithmicVolume: on })
                  }
                />
              </div>
              {(
                [
                  "nextAudioShortcut",
                  "previousAudioShortcut",
                  "rescueShortcut",
                  "mixerShortcut",
                  "quickMixerShortcut",
                  "settingsShortcut",
                  "volumeUpShortcut",
                  "volumeDownShortcut",
                ] as const
              ).map((k, i) => (
                <Field
                  key={k}
                  label={
                    [
                      "Nächster Audiofavorit",
                      "Vorheriger Audiofavorit",
                      "Sichere Konfiguration wiederherstellen",
                      "Audio-Mixer öffnen",
                      "Audio-Schnellmixer öffnen",
                      "Einstellungen öffnen",
                      "Lauter",
                      "Leiser",
                    ][i]
                  }
                >
                  <KeyInput
                    value={preferences[k] ?? null}
                    onChange={(value) =>
                      setPreferences({ ...preferences, [k]: value })
                    }
                  />
                </Field>
              ))}
              <p className="helper">
                Bereits belegte Shortcuts werden gemeldet. Bestehende
                Desktop-Verknüpfungen werden nicht verändert.
              </p>
              <SaveError message={saveError} />
              <Button
                disabled={working}
                onClick={async () => {
                  if (await save({ ...settings, preferences }))
                    setPreferences(null);
                }}
              >
                Einstellungen speichern
              </Button>
            </div>
          )}
        </DialogContent>
      </Dialog>
      {features.liveMonitorAlignment && state.pending?.liveAlignment && (
        <LiveAlignment
          key={state.pending.id}
          transaction={state.pending}
          monitors={current}
          seconds={seconds}
          editableIds={profile?.monitors?.map((monitor) => monitor.deviceId)}
          onError={setAlignmentError}
          refresh={async () => setState(await call<State>("state"))}
          onKeep={(monitors) =>
            setProfile((draft) =>
              draft?.monitors
                ? {
                    ...draft,
                    monitors: draft.monitors.map((monitor) => ({
                      ...monitor,
                      y: monitor.primary
                        ? 0
                        : (monitors.find(
                            (live) => live.deviceId === monitor.deviceId,
                          )?.y ?? monitor.y),
                    })),
                  }
                : draft,
            )
          }
          preview={(monitors) => <MonitorPreview monitors={monitors} />}
        />
      )}
      <Dialog
        open={
          !!state.pending &&
          (!state.pending.liveAlignment || !features.liveMonitorAlignment)
        }
      >
        <DialogContent
          showCloseButton={false}
          onEscapeKeyDown={(e) => e.preventDefault()}
          onPointerDownOutside={(e) => e.preventDefault()}
          onInteractOutside={(e) => e.preventDefault()}
          onKeyDown={(e) => {
            if (
              e.key === "Enter" &&
              state.pending &&
              !working &&
              !state.busy &&
              !state.pending.applying &&
              seconds > 0
            ) {
              e.preventDefault();
              action("confirm", state.pending.id);
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>
              {applyingMonitor
                ? "Monitorwechsel wird angewendet …"
                : "Ist das Bild richtig?"}
            </DialogTitle>
            <DialogDescription>
              {applyingMonitor
                ? "Windows übernimmt die Monitor- und Audioeinstellungen. Die Bestätigung wird danach freigegeben."
                : "Bestätige diese Monitoranordnung. Ohne Bestätigung wird der vorherige Zustand wiederhergestellt."}
            </DialogDescription>
          </DialogHeader>
          {!applyingMonitor && (
            <div className="text-center text-5xl font-semibold py-5">
              {seconds}
              <span className="text-base text-muted-foreground"> s</span>
            </div>
          )}
          {!applyingMonitor && (
            <DialogFooter>
              <Button variant="outline" onClick={() => action("rollback")}>
                Zurücksetzen
              </Button>
              <Button
                autoFocus
                disabled={seconds <= 0}
                onClick={() => action("confirm", state.pending!.id)}
              >
                Behalten · Enter
              </Button>
            </DialogFooter>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
