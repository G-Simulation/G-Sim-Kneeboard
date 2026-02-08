/** @jsx FSComponent.buildComponent */
/** @jsxFrag FSComponent.Fragment */
import {
  App,
  AppBootMode,
  AppInstallProps,
  AppSuspendMode,
  AppView,
  AppViewProps,
  Efb,
  RequiredProps,
  TVNode,
} from "@efb/efb-api";
import { FSComponent, VNode } from "@microsoft/msfs-sdk";
import "./GsimKneeboard.scss";

declare const BASE_URL: string;

const LOCAL_KNEEBOARD_URL = `http://localhost:815/kneeboard.html`;

class GsimKneeboardView extends AppView<RequiredProps<AppViewProps, "bus">> {
  private readonly iframeRef = FSComponent.createRef<HTMLIFrameElement>();
  private readonly fallbackRef = FSComponent.createRef<HTMLDivElement>();
  private probeIntervalId?: number;

  private readonly handleIframeLoad = (): void => {
    this.setConnectionState(true);
  };

  private readonly handleIframeError = (): void => {
    this.setConnectionState(false);
  };

  public onAfterRender(node: VNode): void {
    super.onAfterRender(node);

    // Initial state
    this.setConnectionState(false);

    // Iframe event listeners
    const iframe = this.iframeRef.instance;
    if (iframe) {
      iframe.addEventListener("load", this.handleIframeLoad);
      iframe.addEventListener("error", this.handleIframeError);
    }

    // Probe server availability
    this.startProbing();
  }

  public destroy(): void {
    const iframe = this.iframeRef.instance;
    if (iframe) {
      iframe.removeEventListener("load", this.handleIframeLoad);
      iframe.removeEventListener("error", this.handleIframeError);
    }

    if (this.probeIntervalId !== undefined) {
      window.clearInterval(this.probeIntervalId);
    }

    super.destroy();
  }

  private startProbing(): void {
    // Probe server every 10 seconds
    this.probeIntervalId = window.setInterval(() => {
      fetch(LOCAL_KNEEBOARD_URL, { method: 'HEAD' })
        .then(() => this.setConnectionState(true))
        .catch(() => this.setConnectionState(false));
    }, 10000);
  }

  private setConnectionState(connected: boolean): void {
    const fallback = this.fallbackRef.instance;
    if (fallback) {
      fallback.style.display = connected ? "none" : "flex";
    }

    const iframe = this.iframeRef.instance;
    if (iframe) {
      iframe.style.display = connected ? "block" : "none";
    }
  }

  public render(): VNode {
    return (
      <div class="gsim-kneeboard">
        <div
          class="kneeboard-connection-fallback"
          ref={this.fallbackRef}
        >
          <div
            class="kneeboard-connection-card"
          >
            <img
              src={`${BASE_URL}/Assets/Logo.png`}
              alt="GSim Logo"
              class="kneeboard-connection-logo"
            />
            <span
              class="kneeboard-connection-Span"
            >
              Waiting for Kneeboard server&hellip;
            </span>
          </div>
        </div>
        <iframe
          ref={this.iframeRef}
          src={LOCAL_KNEEBOARD_URL}
          title="GSim Kneeboard"
        />
      </div>
    ) as unknown as VNode;
  }
}

class GsimKneeboard extends App {
  public get name(): string {
    return "Kneeboard";
  }

  public get icon(): string {
    return `${BASE_URL}/Assets/app-icon.svg`;
  }

  public BootMode = AppBootMode.COLD;
  public SuspendMode = AppSuspendMode.SLEEP;

  public async install(_props: AppInstallProps): Promise<void> {
    Efb.loadCss(`${BASE_URL}/GsimKneeboard.css`).catch((err) =>
      console.error("Unable to load kneeboard stylesheet:", err)
    );
    return Promise.resolve();
  }

  public render(): TVNode<GsimKneeboardView> {
    return (
      <GsimKneeboardView bus={this.bus} />
    ) as unknown as TVNode<GsimKneeboardView>;
  }
}

Efb.use(GsimKneeboard);
