import { spawn } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const playwrightModule = process.env.TSPACK_PLAYWRIGHT_MODULE ?? "../../../../tspack/node_modules/playwright";
const { chromium } = require(playwrightModule);
const baseUrl = "http://127.0.0.1:4173";
const artifactDirectory = "artifacts/cts-web-content-fit-m0";
const tolerance = 1;
const server = spawn("node", ["server.mjs"], { stdio: ["ignore", "pipe", "pipe"] });
const diagnostics = { console: [], page: [], request: [] };
const evidence = [];
const attachmentArtifact = JSON.parse(await readFile("dist/browser/attachments.json", "utf8"));
let browser;

try {
  await waitForServer();
  browser = await chromium.launch();
  await mkdir(artifactDirectory, { recursive: true });

  for (const profile of [
    { name: "desktop", layout: "CopelandDesktop", width: 1440, height: 900 },
    { name: "tablet", layout: "CopelandTablet", width: 768, height: 1024 },
    { name: "mobile", layout: "CopelandMobile", width: 390, height: 844 }
  ]) {
    const page = await browser.newPage({ viewport: { width: profile.width, height: profile.height } });
    page.on("console", message => { if (message.type() === "error") diagnostics.console.push(message.text()); });
    page.on("pageerror", error => diagnostics.page.push(error.message));
    page.on("requestfailed", request => diagnostics.request.push({ url: request.url(), error: request.failure()?.errorText ?? "unknown" }));
    page.on("response", response => { if (response.status() >= 400) diagnostics.request.push({ url: response.url(), status: response.status() }); });
    await page.goto(baseUrl, { waitUntil: "networkidle" });
    await page.waitForFunction(layout => document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-title .text-fit-target`) !== null, profile.layout);
    await page.waitForFunction(layout => document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='featureGrid'] [data-copeland-renderer-host='CustomElement'] [data-copeland-attachment]`) !== null, profile.layout);

    const initial = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
    equal(`${profile.name} root width`, initial.root.width, profile.width, initial);
    equal(`${profile.name} root height`, initial.root.height, profile.height, initial);
    if (initial.document.scrollWidth > initial.document.clientWidth + tolerance) throw new Error(`${profile.name} page has horizontal overflow.`);
    if (initial.page.scrollHeight <= initial.page.clientHeight + tolerance) throw new Error(`${profile.name} page does not expose explicit vertical scrolling.`);
    assertInside(`${profile.name} title target`, initial.heroTitleTarget, initial.heroTitle);
    assertInside(`${profile.name} actions`, initial.heroActionsContent, initial.heroActions);
    if (initial.heroTitle.bottom > initial.heroActions.top + tolerance) throw new Error(`${profile.name} title overlaps the action region.`);
    if (initial.heroTitleTarget.scrollWidth > initial.heroTitleTarget.clientWidth + tolerance) throw new Error(`${profile.name} title is horizontally clipped.`);
    if (initial.codeBadge.scrollWidth <= initial.codeBadge.clientWidth + tolerance) throw new Error(`${profile.name} code region did not retain its intentional horizontal scroll extent.`);
    if (!initial.semantics.heroHeading || !initial.semantics.strong || !initial.semantics.link || !initial.semantics.list || !initial.semantics.code) throw new Error(`${profile.name} text document semantic DOM is incomplete.`);
    if (!initial.semantics.customElement) throw new Error(`${profile.name} Custom Element renderer proof did not attach a private shadow subtree.`);
    if (initial.featureCards.length !== 4) throw new Error(`${profile.name} did not render four FeatureCard instances.`);
    for (const card of initial.featureCards) assertInside(`${profile.name} feature card`, card, initial.featureGrid);
    await page.screenshot({ path: `${artifactDirectory}/${profile.name}-initial.png` });
    const plan = attachmentArtifact.plans.find(candidate => candidate.adapterId === "CustomElement" && candidate.hostSelector.includes(`data-machina-layout='${profile.layout}'`));
    if (!plan) throw new Error(`${profile.name} emitted plan artifact has no Custom Element plan.`);
    const statefulBadge = await page.evaluate(async plan => {
      const host = await import("@copeland/browser-v1");
      const before = host.inspectComponentFrame(plan.componentInstanceId);
      const badge = document.querySelector(`[data-copeland-attachment='${plan.attachmentId}']`);
      if (!(badge instanceof HTMLElement)) throw new Error("Missing stateful Custom Element badge.");
      badge.click();
      for (let attempt = 0; attempt < 100; attempt += 1) {
        if (badge.shadowRoot?.querySelector("span")?.textContent === "Custom Elements still work") break;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      const after = host.inspectComponentFrame(plan.componentInstanceId);
      return {
        before,
        after,
        text: badge.shadowRoot?.querySelector("span")?.textContent ?? null,
        lifecycle: host.inspectAttachmentRuntime(plan.attachmentId),
        trace: host.inspectComponentFrameTrace()
      };
    }, plan);
    if (statefulBadge.before?.stateIdentity !== `${plan.componentInstanceId}::state` || statefulBadge.after?.componentInstanceId !== plan.componentInstanceId || statefulBadge.text !== "Custom Elements still work" || statefulBadge.lifecycle.mounts !== 1 || statefulBadge.lifecycle.updates !== 1 || statefulBadge.lifecycle.unmounts !== 0 || !statefulBadge.trace.some(entry => entry.kind === "EventDispatched" && entry.componentInstanceId === plan.componentInstanceId)) {
      throw new Error(`${profile.name} stateful Custom Element badge proof failed: ${JSON.stringify(statefulBadge)}`);
    }
    const dialogPlan = attachmentArtifact.plans.find(candidate => candidate.componentDefinitionId.includes("#DialogHost") && candidate.hostBoxId.startsWith(`${profile.layout}.`));
    if (!dialogPlan) throw new Error(`${profile.name} emitted dialog fixture has no host attachment.`);
    const dialogLifecycle = await page.evaluate(async plan => {
      const host = await import("@copeland/browser-v1");
      const parent = document.querySelector(`[data-copeland-attachment='${plan.attachmentId}']`);
      if (!(parent instanceof HTMLElement)) throw new Error(`Missing DialogHost Custom Element attachment; host=${document.querySelector(plan.hostSelector)?.outerHTML ?? "none"}`);
      const prefix = `${plan.componentInstanceId}::branch-child::`;
      const initialChildren = host.inspectComponentFrameTrace().filter(entry => entry.kind === "ChildFrameCreated" && entry.componentInstanceId.startsWith(prefix));
      parent.click();
      let childId = null;
      for (let attempt = 0; attempt < 100; attempt += 1) {
        childId = host.inspectComponentFrameTrace().find(entry => entry.kind === "ChildFrameCreated" && entry.componentInstanceId.startsWith(prefix))?.componentInstanceId ?? null;
        if (childId !== null) break;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      if (childId === null) throw new Error("DialogHost did not create its Open-branch child frame.");
      const childBeforeClose = host.inspectComponentFrame(childId);
      parent.click();
      for (let attempt = 0; attempt < 100; attempt += 1) {
        if (host.inspectComponentFrame(childId) === null) break;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      let destroyedEvent = null;
      try { host.dispatchComponentEvent(childId, "Confirm"); }
      catch (error) { destroyedEvent = error instanceof Error ? error.message : String(error); }
      return {
        initialChildren: initialChildren.length,
        parentAfter: host.inspectComponentFrame(plan.componentInstanceId),
        childBeforeClose,
        childAfterClose: host.inspectComponentFrame(childId),
        destroyedEvent,
        trace: host.inspectComponentFrameTrace()
      };
    }, dialogPlan);
    if (dialogLifecycle.initialChildren !== 0
      || dialogLifecycle.parentAfter?.componentInstanceId !== dialogPlan.componentInstanceId
      || dialogLifecycle.childBeforeClose === null
      || dialogLifecycle.childAfterClose !== null
      || !dialogLifecycle.destroyedEvent?.includes("COPE-COMPONENT-STATE-0103")) {
      throw new Error(`${profile.name} source DialogHost branch lifecycle failed: ${JSON.stringify(dialogLifecycle)}`);
    }
    const lifecycle = await page.evaluate(async plan => {
      const host = await import("@copeland/browser-v1");
      host.detachRenderer(plan.attachmentId, plan.componentInstanceId);
      const removedAfterDetach = document.querySelector(`[data-copeland-attachment='${plan.attachmentId}']`) === null;
      host.attachRenderer(plan.attachmentId, plan.componentInstanceId, plan.hostSelector, plan.adapterId, plan.payload.tagName, "Mounted Custom Element");
      const mounted = document.querySelector(`[data-copeland-attachment='${plan.attachmentId}']`);
      const mountedText = mounted?.shadowRoot?.querySelector("span")?.textContent ?? null;
      host.updateRenderer(plan.attachmentId, plan.componentInstanceId, plan.hostSelector, "Updated Custom Element");
      const updatedText = mounted?.shadowRoot?.querySelector("span")?.textContent ?? null;
      host.detachRenderer(plan.attachmentId, plan.componentInstanceId);
      const removedAfterCleanup = document.querySelector(`[data-copeland-attachment='${plan.attachmentId}']`) === null;
      return { removedAfterDetach, mountedText, updatedText, removedAfterCleanup };
    }, plan);
    if (!lifecycle.removedAfterDetach || lifecycle.mountedText !== "Mounted Custom Element" || lifecycle.updatedText !== "Updated Custom Element" || !lifecycle.removedAfterCleanup) {
      throw new Error(`${profile.name} Custom Element attachment lifecycle failed: ${JSON.stringify(lifecycle)}`);
    }
    const locality = [];
    for (const fixture of [
      { name: "short", text: "Copeland TS." },
      { name: "long", text: "AI-native TypeScript that gives product teams one inspectable language for browser, CLR, package, template, and table boundaries." },
      { name: "token", text: "copelandcontentfitunbrokencontracttoken0123456789012345678901234567890123456789" }
    ]) {
      await page.evaluate(({ layout, text }) => {
        const target = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-title .text-fit-target`);
        if (!(target instanceof HTMLElement)) throw new Error("Missing text target.");
        target.textContent = text;
      }, { layout: profile.layout, text: fixture.text });
      await page.waitForTimeout(50);
      const changed = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
      equal(`${profile.name} ${fixture.name} title box width`, changed.heroTitle.width, initial.heroTitle.width, changed);
      equal(`${profile.name} ${fixture.name} action box top`, changed.heroActions.top, initial.heroActions.top, changed);
      equal(`${profile.name} ${fixture.name} feature origin`, changed.featureGrid.top, initial.featureGrid.top, changed);
      if (changed.document.scrollWidth > changed.document.clientWidth + tolerance) throw new Error(`${profile.name} ${fixture.name} caused horizontal overflow.`);
      locality.push({ fixture: fixture.name, heroTitle: changed.heroTitle, heroActions: changed.heroActions, featureGrid: changed.featureGrid });
    }

    await page.evaluate(layout => {
      const title = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='featureGrid'] .feature-card h2`);
      if (!(title instanceof HTMLElement)) throw new Error("Missing FeatureCard title.");
      title.textContent = "One local card title mutation";
    }, profile.layout);
    await page.waitForTimeout(50);
    const changedCard = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
    equal(`${profile.name} card mutation grid width`, changedCard.featureGrid.width, initial.featureGrid.width, changedCard);
    for (let index = 1; index < changedCard.featureCards.length; index += 1) {
      equal(`${profile.name} sibling card ${index} left`, changedCard.featureCards[index].left, initial.featureCards[index].left, changedCard);
      equal(`${profile.name} sibling card ${index} top`, changedCard.featureCards[index].top, initial.featureCards[index].top, changedCard);
    }

    await page.evaluate(layout => {
      const surface = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='page']`);
      if (!(surface instanceof HTMLElement)) throw new Error("Missing page scroll surface.");
      surface.scrollTop = surface.scrollHeight;
    }, profile.layout);
    await page.waitForTimeout(50);
    const scrolled = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
    if (scrolled.page.scrollTop <= 0) throw new Error(`${profile.name} page did not move when scrolled.`);
    assertInside(`${profile.name} footer after scroll`, scrolled.footer, scrolled.page);
    await page.screenshot({ path: `${artifactDirectory}/${profile.name}-footer.png` });
    evidence.push({ profile, initial, locality, scrolled });
    await page.close();
  }

  await runReactHostReplacementProof();
  await runStateSelectedChildFrameProof();

  if (diagnostics.console.length || diagnostics.page.length || diagnostics.request.length) throw new Error(`Browser diagnostics: ${JSON.stringify(diagnostics)}`);
  await writeFile(`${artifactDirectory}/report.json`, `${JSON.stringify({ success: true, tolerance, diagnostics, evidence }, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify({ success: true, profiles: evidence.map(item => item.profile.name) }, null, 2)}\n`);
} finally {
  await browser?.close();
  if (!server.killed) server.kill();
}

async function runReactHostReplacementProof() {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on("console", message => { if (message.type() === "error") diagnostics.console.push(message.text()); });
  page.on("pageerror", error => diagnostics.page.push(error.message));
  page.on("requestfailed", request => diagnostics.request.push({ url: request.url(), error: request.failure()?.errorText ?? "unknown" }));
  page.on("response", response => { if (response.status() >= 400) diagnostics.request.push({ url: response.url(), status: response.status() }); });
  await page.goto(baseUrl, { waitUntil: "networkidle" });
  const result = await page.evaluate(async () => {
    const hostRuntime = await import("@copeland/browser-v1");
    const React = await import("react");
    const { createRoot } = await import("react-dom/client");
    const attachmentId = "browser-proof::semantic-recovery::attachment";
    const componentInstanceId = "browser-proof::semantic-recovery";
    const plan = {
      attachmentId,
      componentDefinitionId: "browser-proof::definition",
      componentInstanceId,
      parentComponentInstanceId: null,
      hostBoxId: "BrowserProof.recoveryHost",
      hostSelector: "[data-copeland-recovery-host='stable']",
      adapterId: "CustomElement",
      requiredHostCapabilities: ["RendererAttachment", "StableMountPoint"],
      requiredContentCapabilities: ["CustomElement"],
      payloadContract: "custom-element-bridge",
      payload: { tagName: "copeland-renderer-badge", label: "Recovery initial" },
      lifecycle: { mount: true, update: true, unmount: true },
      source: { path: "browser-proof", line: 1, column: 1, provenance: "runtime" }
    };
    hostRuntime.registerComponentFrames([{
      componentInstanceId,
      componentDefinitionId: "browser-proof::definition",
      parentComponentInstanceId: null,
      stateIdentity: `${componentInstanceId}::state`,
      initialState: "Recovery initial",
      attachmentIds: [attachmentId],
      eventContracts: {
        Confirm: { payload: "void", transition: () => "Recovery after state" }
      },
      project: (state, plans) => plans.map(candidate => ({ ...candidate, payload: { ...candidate.payload, label: state } }))
    }]);
    const shell = document.createElement("section");
    document.body.appendChild(shell);
    const root = createRoot(shell);
    const renderHost = tag => root.render(React.createElement(tag, { "data-copeland-recovery-host": "stable" }));
    const waitFor = async predicate => {
      for (let attempt = 0; attempt < 100; attempt += 1) {
        if (predicate()) return;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      throw new Error("Timed out waiting for recovery runtime state.");
    };

    hostRuntime.registerAttachmentPlans({ schemaVersion: 1, projectId: "browser-proof", plans: [plan] });
    renderHost("span");
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${attachmentId}']`) !== null);
    const oldHost = document.querySelector(plan.hostSelector);
    const oldElement = document.querySelector(`[data-copeland-attachment='${attachmentId}']`);
    renderHost("div");
    await waitFor(() => {
      const current = document.querySelector(`[data-copeland-attachment='${attachmentId}']`);
      return current !== null && current !== oldElement && document.querySelector(plan.hostSelector) !== oldHost;
    });
    const recovered = document.querySelector(`[data-copeland-attachment='${attachmentId}']`);
    const recoveredText = recovered?.shadowRoot?.querySelector("span")?.textContent ?? null;
    hostRuntime.dispatchComponentEvent(componentInstanceId, "Confirm");
    await waitFor(() => recovered?.shadowRoot?.querySelector("span")?.textContent === "Recovery after state");
    const countsAfterUpdate = hostRuntime.inspectAttachmentRuntime(attachmentId);
    hostRuntime.registerAttachmentPlans({ schemaVersion: 1, projectId: "browser-proof", plans: [] });
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${attachmentId}']`) === null);
    const finalCounts = hostRuntime.inspectAttachmentRuntime(attachmentId);
    hostRuntime.destroyComponentFrame(componentInstanceId);
    const frameAfterDestroy = hostRuntime.inspectComponentFrame(componentInstanceId);
    root.unmount();
    shell.remove();
    return {
      oldHostConnected: oldHost?.isConnected ?? true,
      oldElementConnected: oldElement?.isConnected ?? true,
      recoveredText,
      liveCount: document.querySelectorAll(`[data-copeland-attachment='${attachmentId}']`).length,
      countsAfterUpdate,
      finalCounts,
      frameAfterDestroy
    };
  });
  if (result.oldHostConnected || result.oldElementConnected || result.recoveredText !== "Recovery initial" || result.liveCount !== 0 || result.countsAfterUpdate.mounts !== 2 || result.countsAfterUpdate.updates !== 1 || result.countsAfterUpdate.unmounts !== 1 || result.finalCounts.unmounts !== 2 || result.finalCounts.mounted || result.finalCounts.pending || result.frameAfterDestroy !== null) {
    throw new Error(`React semantic host replacement proof failed: ${JSON.stringify(result)}`);
  }
  await page.close();
}

async function runStateSelectedChildFrameProof() {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on("console", message => { if (message.type() === "error") diagnostics.console.push(message.text()); });
  page.on("pageerror", error => diagnostics.page.push(error.message));
  await page.goto(baseUrl, { waitUntil: "networkidle" });
  const result = await page.evaluate(async () => {
    const hostRuntime = await import("@copeland/browser-v1");
    const parentId = "browser-proof::dialog-host";
    const childId = `${parentId}::branch-child::DialogHost::presentation-branch::Open::0::call::0`;
    const parentAttachmentId = `${parentId}::attachment`;
    const childAttachmentId = `${childId}::attachment`;
    const shell = document.createElement("section");
    shell.setAttribute("data-copeland-dialog-host", "stable");
    document.body.appendChild(shell);
    const hostSelector = "[data-copeland-dialog-host='stable']";
    const parentPlan = {
      attachmentId: parentAttachmentId,
      componentDefinitionId: "browser-proof::DialogHost",
      componentInstanceId: parentId,
      parentComponentInstanceId: null,
      hostBoxId: "BrowserProof.dialogHost",
      hostSelector,
      adapterId: "CustomElement",
      requiredHostCapabilities: ["RendererAttachment", "StableMountPoint"],
      requiredContentCapabilities: ["CustomElement"],
      payloadContract: "custom-element-bridge",
      payload: { tagName: "copeland-renderer-badge", label: "Open dialog" },
      lifecycle: { mount: true, update: true, unmount: true },
      source: { path: "browser-proof", line: 1, column: 1, provenance: "runtime" }
    };
    const childFrame = state => ({
      componentInstanceId: childId,
      componentDefinitionId: "browser-proof::ConfirmDialog",
      parentComponentInstanceId: parentId,
      stateIdentity: `${childId}::state`,
      initialState: "Ready",
      attachmentIds: [childAttachmentId],
      eventContracts: { Confirm: { payload: "void", transition: () => "Confirmed" } },
      rendererEventName: "Confirm",
      project: (next, plans) => plans.map(plan => ({ ...plan, payload: { ...plan.payload, label: next } })),
      plans: [{
        ...parentPlan,
        attachmentId: childAttachmentId,
        componentDefinitionId: "browser-proof::ConfirmDialog",
        componentInstanceId: childId,
        parentComponentInstanceId: parentId,
        payload: { tagName: "copeland-renderer-badge", label: state === "OpenUpdated" ? "Confirm updated" : "Confirm dialog" }
      }]
    });
    const waitFor = async predicate => {
      for (let attempt = 0; attempt < 100; attempt += 1) {
        if (predicate()) return;
        await new Promise(resolve => setTimeout(resolve, 10));
      }
      throw new Error("Timed out waiting for state-selected child frame lifecycle.");
    };

    hostRuntime.registerComponentFrames([{
      componentInstanceId: parentId,
      componentDefinitionId: "browser-proof::DialogHost",
      parentComponentInstanceId: null,
      stateIdentity: `${parentId}::state`,
      initialState: "Closed",
      attachmentIds: [parentAttachmentId],
      eventContracts: {
        Open: { payload: "void", transition: () => "Open" },
        Refresh: { payload: "void", transition: () => "OpenUpdated" },
        Close: { payload: "void", transition: () => "Closed" }
      },
      rendererEventName: "Open",
      project: (state, plans) => ({
        plans: plans.map(plan => ({ ...plan, payload: { ...plan.payload, label: state === "Closed" ? "Open dialog" : "Close dialog" } })),
        frames: state === "Closed" ? [] : [childFrame(state)]
      })
    }]);
    hostRuntime.registerAttachmentPlans({ schemaVersion: 1, projectId: "browser-proof", plans: [parentPlan] });
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${parentAttachmentId}']`) !== null);
    const parentBefore = hostRuntime.inspectComponentFrame(parentId);
    const closedChild = hostRuntime.inspectComponentFrame(childId);
    document.querySelector(`[data-copeland-attachment='${parentAttachmentId}']`)?.click();
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${childAttachmentId}']`) !== null);
    const parentAfterOpen = hostRuntime.inspectComponentFrame(parentId);
    const childAfterOpen = hostRuntime.inspectComponentFrame(childId);
    hostRuntime.dispatchComponentEvent(parentId, "Refresh");
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${childAttachmentId}']`)?.shadowRoot?.querySelector("span")?.textContent === "Confirm updated");
    const childAfterRefresh = hostRuntime.inspectComponentFrame(childId);
    const refreshedCounts = hostRuntime.inspectAttachmentRuntime(childAttachmentId);
    hostRuntime.dispatchComponentEvent(parentId, "Close");
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${childAttachmentId}']`) === null);
    let destroyedEvent = null;
    try { hostRuntime.dispatchComponentEvent(childId, "Confirm"); }
    catch (error) { destroyedEvent = error instanceof Error ? error.message : String(error); }
    const childAfterClose = hostRuntime.inspectComponentFrame(childId);
    const closedCounts = hostRuntime.inspectAttachmentRuntime(childAttachmentId);
    hostRuntime.dispatchComponentEvent(parentId, "Open");
    await waitFor(() => document.querySelector(`[data-copeland-attachment='${childAttachmentId}']`) !== null);
    const childAfterReopen = hostRuntime.inspectComponentFrame(childId);
    const reopenedCounts = hostRuntime.inspectAttachmentRuntime(childAttachmentId);
    const trace = hostRuntime.inspectComponentFrameTrace();
    hostRuntime.destroyComponentFrame(parentId);
    shell.remove();
    return {
      parentBefore, closedChild, parentAfterOpen, childAfterOpen, childAfterRefresh,
      refreshedCounts, childAfterClose, closedCounts, destroyedEvent,
      childAfterReopen, reopenedCounts, trace
    };
  });
  if (result.closedChild !== null
    || result.parentBefore?.componentInstanceId !== result.parentAfterOpen?.componentInstanceId
    || result.childAfterOpen?.componentInstanceId !== result.childAfterRefresh?.componentInstanceId
    || result.refreshedCounts.mounts !== 1 || result.refreshedCounts.updates !== 1
    || result.childAfterClose !== null || result.closedCounts.unmounts !== 1
    || !result.destroyedEvent?.includes("COPE-COMPONENT-STATE-0103")
    || result.childAfterReopen?.componentInstanceId !== result.childAfterOpen?.componentInstanceId
    || result.reopenedCounts.mounts !== 2
    || !result.trace.some(entry => entry.kind === "ChildFrameCreated")
    || !result.trace.some(entry => entry.kind === "ChildFrameDestroyed")) {
    throw new Error(`State-selected child frame proof failed: ${JSON.stringify(result)}`);
  }
  await page.close();
}

function snapshot(layout) {
  const requireElement = selector => {
    const element = document.querySelector(selector);
    if (!(element instanceof HTMLElement)) throw new Error(`Missing element ${selector}.`);
    return element;
  };
  const rectangle = element => {
    const rect = element.getBoundingClientRect();
    return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
  };
  const box = name => rectangle(requireElement(`[data-machina-layout='${layout}'][data-machina-box='${name}']`));
  const heroHost = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero']`);
  const heroTitleHost = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-title`);
  const heroActionsHost = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-actions`);
  const titleTarget = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-title .text-fit-target`);
  const actionsContent = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero'] .hero-actions`);
  const page = requireElement(`[data-machina-layout='${layout}'][data-machina-box='page']`);
  const code = requireElement(`[data-machina-layout='${layout}'][data-machina-box='hero'] .code-badge`);
  const architecture = requireElement(`[data-machina-layout='${layout}'][data-machina-box='architecture']`);
  const footer = requireElement(`[data-machina-layout='${layout}'][data-machina-box='footer']`);
  const featureGrid = box("featureGrid");
  const featureCards = Array.from(document.querySelectorAll(`[data-machina-layout='${layout}'][data-machina-box='featureGrid'] .feature-card`)).map(rectangle);
  const customElement = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='featureGrid'] copeland-renderer-badge`);
  return {
    root: box("root"), page: { ...rectangle(page), scrollHeight: page.scrollHeight, clientHeight: page.clientHeight, scrollTop: page.scrollTop }, content: box("content"), hero: rectangle(heroHost), heroTitle: rectangle(heroTitleHost), heroActions: rectangle(heroActionsHost), featureGrid, featureCards, heroTitleTarget: { ...rectangle(titleTarget), scrollWidth: titleTarget.scrollWidth, clientWidth: titleTarget.clientWidth }, heroActionsContent: rectangle(actionsContent), codeBadge: { ...rectangle(code), scrollWidth: code.scrollWidth, clientWidth: code.clientWidth }, footer: box("footer"), document: { scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }, semantics: { heroHeading: heroTitleHost.querySelector("h1") !== null, strong: document.querySelector("strong") !== null, link: footer.querySelector("a[href='#architecture']") !== null, list: architecture.querySelector("ul > li") !== null, code: code.querySelector("pre") !== null, customElement: customElement instanceof HTMLElement && customElement.hasAttribute("data-copeland-attachment") && customElement.shadowRoot?.querySelector("span")?.textContent?.startsWith("Custom Elements work") === true }
  };
}

function assertInside(label, inner, outer) {
  if (inner.left < outer.left - tolerance || inner.right > outer.right + tolerance || inner.top < outer.top - tolerance || inner.bottom > outer.bottom + tolerance) throw new Error(`${label} escapes its assigned box: ${JSON.stringify({ inner, outer })}`);
}

function equal(label, actual, expected, context) {
  if (Math.abs(actual - expected) > tolerance) throw new Error(`${label}: expected ${expected}, received ${actual}; ${JSON.stringify(context)}`);
}

async function waitForServer() {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    try {
      const response = await fetch(baseUrl);
      if (response.ok) return;
    } catch { }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error("Timed out waiting for the Copeland website host.");
}
