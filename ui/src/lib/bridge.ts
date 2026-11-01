import packageInfo from "../../package.json";
import features from "../../../features.json";
export const appVersion = packageInfo.version;

export type DisplaySetting = {
  deviceId: string;
  name: string;
  width: number;
  height: number;
  x: number;
  y: number;
  rotation: number;
  refreshRate: number;
  scale: number;
  primary: boolean;
};
export type DisplayDevice = {
  id: string;
  name: string;
  connected: boolean;
  active: boolean;
  current: DisplaySetting | null;
  modes?:
    | {
        width: number;
        height: number;
        refreshRate: number;
        recommended: boolean;
      }[]
    | null;
};
export type AudioDevice = {
  id: string;
  name: string;
  input: boolean;
  available: boolean;
  default: boolean;
  communications: boolean;
  volume: number;
  muted: boolean;
};
export type Endpoint = {
  deviceId: string;
  volume: number | null;
  muted: boolean | null;
};
export type Favorite = {
  id: string;
  name: string;
  output: Endpoint | null;
  input: Endpoint | null;
  communicationsOutput: string | null;
  communicationsInput: string | null;
  shortcut: string | null;
};
export type Profile = {
  id: string;
  name: string;
  monitors: DisplaySetting[] | null;
  audioFavoriteId: string | null;
  shortcut: string | null;
  verifiedHash: string | null;
  audio?: {
    output: Endpoint | null;
    input: Endpoint | null;
    name: string | null;
  } | null;
};

export function profileAudio(profile: Profile | undefined, settings: Settings) {
  if (profile?.audio)
    return {
      ...profile.audio,
      communicationsOutput: null,
      communicationsInput: null,
    };
  return settings.audioFavorites.find((f) => f.id === profile?.audioFavoriteId);
}

export function profileAudioName(
  profile: Profile,
  settings: Settings,
  devices: AudioDevice[],
) {
  const audio = profileAudio(profile, settings);
  if (!audio) return "Audio unverändert";
  return (
    audio.name?.trim() ||
    [audio.output, audio.input]
      .filter((e) => e !== null)
      .map(
        (e) =>
          devices.find((d) => d.id === e!.deviceId)?.name ||
          "Gespeichertes Audiogerät",
      )
      .join(" · ")
  );
}
export type Preferences = {
  theme: "system" | "dark" | "light";
  startWithWindows: boolean;
  startMinimized: boolean;
  closeToTray: boolean;
  nextAudioShortcut: string | null;
  previousAudioShortcut: string | null;
  rescueShortcut: string | null;
  mixerShortcut: string | null;
  quickMixerShortcut: string | null;
  settingsShortcut: string | null;
  volumeUpShortcut: string | null;
  volumeDownShortcut: string | null;
  trayScrollVolume: boolean;
  logarithmicVolume: boolean;
};
export type MixerDevice = {
  id: string;
  name: string;
  input: boolean;
  volume: number;
  muted: boolean;
  default: boolean;
  communications: boolean;
  peak: number;
};
export type MixerSession = {
  id: string;
  deviceId: string;
  processId: number;
  appId: string;
  name: string;
  systemSounds: boolean;
  volume: number;
  muted: boolean;
  peak: number;
  routedDeviceId: string | null;
};
export type MixerSnapshot = {
  devices: MixerDevice[];
  sessions: MixerSession[];
  routingAvailable: boolean;
  routingWarning: string | null;
};
export type BluetoothAdapter = {
  id: string;
  name: string;
  enabled: boolean | null;
  started: boolean;
  problemCode: number | null;
};
export type HomeAssistantOptions = {
  enabled: boolean;
  host: string;
  port: number;
  username: string;
  useTls: boolean;
  deviceName: string;
  instanceId: string;
  discoveryPrefix: string;
};
export type HomeAssistantInfo = {
  options: HomeAssistantOptions;
  passwordSaved: boolean;
  connection: { connected: boolean; state: string; error: string | null };
  availableSetups: string[];
};
export type UpdateInfo = {
  currentVersion: string;
  status: string;
  message: string;
  automatic: boolean;
  lastChecked: string | null;
  latestVersion: string | null;
  releaseUrl: string | null;
  progress: number | null;
};
export type Settings = {
  version: number;
  profiles: Profile[];
  audioFavorites: Favorite[];
  preferences: Preferences;
};
export type State = {
  settings: Settings;
  inventory: { displays: DisplayDevice[]; audio: AudioDevice[] };
  pending: {
    id: string;
    deadline: string;
    liveAlignment?: boolean;
    applying?: boolean;
    requestedDisplays?: DisplaySetting[];
    displays?: DisplaySetting[];
  } | null;
  busy: boolean;
  busyMixer?: boolean;
  activeProfile: string | null;
  demo: boolean;
  warning: string | null;
  visible?: boolean;
  requestedView?: string | null;
};
type Message = {
  id?: string;
  result?: unknown;
  error?: string;
  event?: string;
  scope?: string;
  data?: unknown;
};
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (value: unknown) => void;
        addEventListener: (
          name: string,
          cb: (e: { data: Message }) => void,
        ) => void;
      };
    };
  }
}
const callbacks = new Map<
  string,
  {
    resolve: (value: any) => void;
    reject: (e: Error) => void;
    timer: ReturnType<typeof setTimeout>;
  }
>();
const listeners = new Set<(message: Message) => void>();
window.chrome?.webview?.addEventListener("message", ({ data }) => {
  if (data.id && callbacks.has(data.id)) {
    const cb = callbacks.get(data.id)!;
    clearTimeout(cb.timer);
    callbacks.delete(data.id);
    data.error ? cb.reject(new Error(data.error)) : cb.resolve(data.result);
  } else listeners.forEach((fn) => fn(data));
});
export function subscribe(cb: (message: Message) => void) {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}
export async function call<T = unknown>(
  method: string,
  payload: unknown = null,
): Promise<T> {
  if (
    !features.liveMonitorAlignment &&
    (method === "beginAlignment" || method === "updateAlignment")
  )
    throw new Error("Der Live-Höhenabgleich ist vorübergehend deaktiviert.");
  if (!window.chrome?.webview) return demoCall(method, payload) as T;
  const id = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      callbacks.delete(id);
      reject(new Error("Die Aktion dauert zu lange. Bitte Status prüfen."));
    }, 120000);
    callbacks.set(id, { resolve, reject, timer });
    window.chrome!.webview!.postMessage({ id, method, payload });
  });
}
const defaultSettings: Settings = {
  version: 1,
  profiles: [],
  audioFavorites: [],
  preferences: {
    theme: "system",
    startWithWindows: false,
    startMinimized: true,
    closeToTray: true,
    nextAudioShortcut: null,
    previousAudioShortcut: null,
    rescueShortcut: null,
    mixerShortcut: null,
    quickMixerShortcut: null,
    settingsShortcut: null,
    volumeUpShortcut: null,
    volumeDownShortcut: null,
    trayScrollVolume: false,
    logarithmicVolume: false,
  },
};
let demo: State = {
  settings: JSON.parse(
    localStorage.getItem("setuppulse-demo") ||
      localStorage.getItem("setupswitch-demo") ||
      JSON.stringify(defaultSettings),
  ),
  inventory: {
    displays: [
      {
        id: "demo-screen",
        name: "Demo Monitor",
        connected: true,
        active: true,
        modes: [
          { width: 2560, height: 1440, refreshRate: 60, recommended: true },
          { width: 1920, height: 1080, refreshRate: 60, recommended: false },
        ],
        current: {
          deviceId: "demo-screen",
          name: "Demo Monitor",
          width: 2560,
          height: 1440,
          x: 0,
          y: 0,
          rotation: 1,
          refreshRate: 60,
          scale: 100,
          primary: true,
        },
      },
    ],
    audio: [
      {
        id: "demo-speakers",
        name: "Demo Lautsprecher",
        input: false,
        available: true,
        default: true,
        communications: true,
        volume: 50,
        muted: false,
      },
      {
        id: "demo-headset",
        name: "Demo Headset",
        input: false,
        available: true,
        default: false,
        communications: false,
        volume: 35,
        muted: false,
      },
      {
        id: "demo-mic",
        name: "Demo Mikrofon",
        input: true,
        available: true,
        default: true,
        communications: true,
        volume: 70,
        muted: false,
      },
    ],
  },
  pending: null,
  busy: false,
  activeProfile: null,
  demo: true,
  warning: null,
};
if (new URLSearchParams(location.search).get("demoMonitors") === "3") {
  const primary = demo.inventory.displays[0];
  for (const [id, name, x] of [
    ["demo-left", "Demo Links", -1920],
    ["demo-right", "Demo Rechts", 2560],
  ] as const) {
    demo.inventory.displays.push({
      ...structuredClone(primary),
      id,
      name,
      current: {
        ...primary.current!,
        deviceId: id,
        name,
        width: 1920,
        height: 1080,
        x,
        y: 100,
        primary: false,
      },
    });
  }
}
let alignmentOriginal: DisplayDevice[] | null = null;
let demoMixerSessions: MixerSession[] = [
  {
    id: "browser-tab",
    deviceId: "demo-speakers",
    processId: 101,
    appId: "demo-browser",
    name: "Demo Browser",
    systemSounds: false,
    volume: 60,
    muted: false,
    peak: 0.24,
    routedDeviceId: null,
  },
  {
    id: "browser-audio",
    deviceId: "demo-speakers",
    processId: 102,
    appId: "demo-browser",
    name: "Demo Browser",
    systemSounds: false,
    volume: 60,
    muted: false,
    peak: 0.12,
    routedDeviceId: null,
  },
  {
    id: "music",
    deviceId: "demo-headset",
    processId: 201,
    appId: "demo-music",
    name: "Demo Musik",
    systemSounds: false,
    volume: 35,
    muted: false,
    peak: 0.45,
    routedDeviceId: "demo-headset",
  },
  {
    id: "system",
    deviceId: "demo-speakers",
    processId: 0,
    appId: "system",
    name: "Systemsounds",
    systemSounds: true,
    volume: 100,
    muted: false,
    peak: 0,
    routedDeviceId: null,
  },
];
function mixerSnapshot(): MixerSnapshot {
  const standard = demo.inventory.audio.find(
    (device) => !device.input && device.default,
  )?.id;
  demoMixerSessions = demoMixerSessions.map((session) =>
    session.routedDeviceId === null && standard
      ? { ...session, deviceId: standard }
      : session,
  );
  return {
    devices: demo.inventory.audio
      .filter((device) => device.available)
      .map((device) => ({
        ...device,
        peak: device.muted ? 0 : device.input ? 0.08 : 0.35,
      })),
    sessions: structuredClone(demoMixerSessions),
    routingAvailable: true,
    routingWarning: null,
  };
}
let alignmentTimer: ReturnType<typeof setTimeout> | null = null;
function resetAlignmentTimer() {
  if (alignmentTimer) clearTimeout(alignmentTimer);
  alignmentTimer = setTimeout(() => {
    if (!demo.pending?.liveAlignment) return;
    if (alignmentOriginal) demo.inventory.displays = alignmentOriginal;
    alignmentOriginal = null;
    demo.pending = null;
    listeners.forEach((fn) =>
      fn({ event: "state", data: structuredClone(demo) }),
    );
  }, 90000);
}
function applyDemoAudio(audio: NonNullable<ReturnType<typeof profileAudio>>) {
  for (const [endpoint, input] of [
    [audio.output, false],
    [audio.input, true],
  ] as const)
    if (
      endpoint &&
      !demo.inventory.audio.some(
        (d) => d.id === endpoint.deviceId && d.input === input && d.available,
      )
    )
      throw new Error(
        "Ein benötigtes Audiogerät fehlt oder ist noch nicht bereit.",
      );
  demo.inventory.audio = demo.inventory.audio.map((d) => {
    const endpoint = d.input ? audio.input : audio.output;
    return {
      ...d,
      default: endpoint ? d.id === endpoint.deviceId : d.default,
      volume:
        endpoint?.deviceId === d.id ? (endpoint.volume ?? d.volume) : d.volume,
      muted:
        endpoint?.deviceId === d.id ? (endpoint.muted ?? d.muted) : d.muted,
    };
  });
}

let demoHomeAssistant: HomeAssistantInfo = {
  options: {
    enabled: false,
    host: "",
    port: 1883,
    username: "",
    useTls: false,
    deviceName: "SetupPulse Demo",
    instanceId: "00000000000000000000000000000000",
    discoveryPrefix: "homeassistant",
  },
  passwordSaved: false,
  connection: { connected: false, state: "Deaktiviert", error: null },
  availableSetups: [],
};
let demoUpdates: UpdateInfo = {
  currentVersion: appVersion,
  status: "demo",
  message: "Demo – keine Update-Verbindung.",
  automatic: true,
  lastChecked: null,
  latestVersion: null,
  releaseUrl: null,
  progress: null,
};
async function demoCall(method: string, payload: any): Promise<any> {
  if (method === "updates" || method === "checkUpdate")
    return structuredClone(demoUpdates);
  if (method === "updateAutomatic") {
    demoUpdates = { ...demoUpdates, automatic: !!payload };
    return structuredClone(demoUpdates);
  }
  if (method === "installUpdate")
    throw new Error("In der Demo werden keine Updates installiert.");
  if (method === "homeAssistantSettings")
    return structuredClone(demoHomeAssistant);
  if (method === "saveHomeAssistant") {
    if (payload.options.enabled && !payload.options.host.trim())
      throw new Error("Bitte den MQTT-Hostnamen eingeben.");
    demoHomeAssistant = {
      ...demoHomeAssistant,
      options: {
        ...payload.options,
        instanceId: demoHomeAssistant.options.instanceId,
      },
      passwordSaved:
        Boolean(payload.password) ||
        (!payload.clearPassword && demoHomeAssistant.passwordSaved),
      connection: {
        connected: false,
        state: payload.options.enabled
          ? "Demo – keine Verbindung"
          : "Deaktiviert",
        error: null,
      },
    };
    return structuredClone(demoHomeAssistant);
  }
  if (method === "bluetoothState")
    return [
      {
        id: "demo-bluetooth-1",
        name: "Demo-Adapter 1",
        enabled: true,
        started: true,
        problemCode: 0,
      },
      {
        id: "demo-bluetooth-2",
        name: "Demo-Adapter 2",
        enabled: false,
        started: false,
        problemCode: 22,
      },
      {
        id: "demo-bluetooth-3",
        name: "Demo-Adapter 3",
        enabled: true,
        started: false,
        problemCode: 31,
      },
    ];
  if (method === "mixerState") return mixerSnapshot();
  if (["mixerEndpoint", "mixerSession", "mixerRoute"].includes(method)) {
    if (demo.pending || demo.busy)
      throw new Error("Bitte zuerst den laufenden Monitorwechsel abschließen.");
    if (
      payload.volume !== undefined &&
      (!Number.isFinite(payload.volume) ||
        payload.volume < 0 ||
        payload.volume > 100)
    )
      throw new Error("Lautstärke muss zwischen 0 und 100 liegen.");
    const device = demo.inventory.audio.find(
      (device) => device.id === payload.deviceId && device.available,
    );
    if (!device) throw new Error("Audiogerät nicht mehr verfügbar.");
    if (method === "mixerEndpoint") {
      demo.inventory.audio = demo.inventory.audio.map((item) => ({
        ...item,
        ...(item.id === device.id
          ? {
              volume: payload.volume ?? item.volume,
              muted: payload.muted ?? item.muted,
            }
          : {}),
        ...(payload.defaultRole === "standard" && item.input === device.input
          ? { default: item.id === device.id }
          : {}),
        ...(payload.defaultRole === "communications" &&
        item.input === device.input
          ? { communications: item.id === device.id }
          : {}),
      }));
    } else {
      mixerSnapshot();
      const session = demoMixerSessions.find(
        (session) =>
          session.deviceId === payload.deviceId &&
          session.id === payload.sessionId &&
          session.processId === payload.processId,
      );
      if (!session)
        throw new Error(
          "Die Audiositzung wurde beendet. Bitte Mixer aktualisieren.",
        );
      if (method === "mixerSession")
        demoMixerSessions = demoMixerSessions.map((item) =>
          item === session
            ? {
                ...item,
                volume: payload.volume ?? item.volume,
                muted: payload.muted ?? item.muted,
              }
            : item,
        );
      else {
        if (session.systemSounds || !session.processId)
          throw new Error(
            "Systemsounds können keinem App-Ausgang zugeordnet werden.",
          );
        const target =
          payload.targetDeviceId === null
            ? demo.inventory.audio.find(
                (item) => !item.input && item.default && item.available,
              )
            : demo.inventory.audio.find(
                (item) =>
                  item.id === payload.targetDeviceId &&
                  !item.input &&
                  item.available,
              );
        if (!target) throw new Error("Ausgabegerät nicht verfügbar.");
        demoMixerSessions = demoMixerSessions.map((item) =>
          item.processId === session.processId
            ? {
                ...item,
                deviceId: target.id,
                routedDeviceId: payload.targetDeviceId,
              }
            : item,
        );
      }
    }
  }
  if (method === "openAudioSettings")
    throw new Error(
      "Windows-Audioeinstellungen sind in der Windows-App verfügbar.",
    );
  if (method === "licenses")
    return "Browser-Demo. Die vollständigen MIT- und Drittanbieter-Lizenzen sind in der Windows-App unter Einstellungen → Über SetupPulse enthalten.";
  if (method === "state") return structuredClone(demo);
  if (method === "beginAlignment") {
    if (demo.pending) throw new Error("Ein Monitortest läuft bereits.");
    alignmentOriginal = structuredClone(demo.inventory.displays);
    const original = demo.inventory.displays
      .filter((display) => display.active)
      .map((display) => display.current!);
    if (original.length < 2)
      throw new Error("Mindestens zwei aktive Monitore erforderlich.");
    demo.pending = {
      id: crypto.randomUUID(),
      deadline: new Date(Date.now() + 90000).toISOString(),
      liveAlignment: true,
      requestedDisplays: structuredClone(original),
      displays: structuredClone(original),
    };
    demo.activeProfile = null;
    resetAlignmentTimer();
  }
  if (method === "updateAlignment") {
    if (
      !demo.pending?.liveAlignment ||
      demo.pending.id !== payload.id ||
      Date.now() >= Date.parse(demo.pending.deadline)
    )
      throw new Error("Live-Abgleich ist nicht mehr aktiv.");
    const requested = payload.monitors as DisplaySetting[];
    if (requested.some((monitor) => monitor.primary && monitor.y !== 0))
      throw new Error("Hauptmonitor muss bei 0 bleiben.");
    for (const monitor of requested) {
      if (!Number.isInteger(monitor.y) || Math.abs(monitor.y) > 32768)
        throw new Error("Ungültige Y-Position.");
      const neighbors = requested.filter(
        (other) =>
          other.deviceId !== monitor.deviceId &&
          (other.x + other.width === monitor.x ||
            monitor.x + monitor.width === other.x),
      );
      if (
        neighbors.length &&
        !neighbors.some(
          (other) =>
            Math.max(monitor.y, other.y) <
            Math.min(monitor.y + monitor.height, other.y + other.height),
        )
      )
        throw new Error(
          "Monitore müssen an einer gemeinsamen Kante verbunden sein.",
        );
    }
    demo.inventory.displays = demo.inventory.displays.map((display) => ({
      ...display,
      current:
        requested.find((monitor) => monitor.deviceId === display.id) ??
        display.current,
    }));
    demo.pending = {
      ...demo.pending,
      requestedDisplays: structuredClone(requested),
      deadline: new Date(Date.now() + 90000).toISOString(),
    };
    resetAlignmentTimer();
  }
  if (method === "save") {
    demo.settings = payload;
    localStorage.setItem("setuppulse-demo", JSON.stringify(payload));
  }
  if (method === "applyAudio") {
    const f = demo.settings.audioFavorites.find((f) => f.id === payload)!;
    applyDemoAudio(f);
  }
  if (method === "applyProfile") {
    const p = demo.settings.profiles.find((p) => p.id === payload)!;
    demo.activeProfile = p.id;
    if (p.monitors)
      demo.pending = {
        id: crypto.randomUUID(),
        deadline: new Date(Date.now() + 90000).toISOString(),
      };
    const audio = profileAudio(p, demo.settings);
    if (audio) applyDemoAudio(audio);
  }
  if (method === "confirm") {
    if (!demo.pending?.liveAlignment)
      demo.settings.profiles = demo.settings.profiles.map((p) =>
        p.id === demo.activeProfile ? { ...p, verifiedHash: "demo-only" } : p,
      );
    localStorage.setItem("setuppulse-demo", JSON.stringify(demo.settings));
    demo.pending = null;
    alignmentOriginal = null;
    if (alignmentTimer) clearTimeout(alignmentTimer);
  }
  if (method === "rollback") {
    if (demo.pending?.liveAlignment && alignmentOriginal)
      demo.inventory.displays = alignmentOriginal;
    demo.pending = null;
    alignmentOriginal = null;
    if (alignmentTimer) clearTimeout(alignmentTimer);
  }
  if (method === "rescue") demo.activeProfile = null;
  if (["import", "export", "exit"].includes(method))
    throw new Error("Diese Funktion ist in der Windows-App verfügbar.");
  listeners.forEach((fn) =>
    fn({ event: "state", data: structuredClone(demo) }),
  );
  return true;
}
