// Lightweight pan/zoom for the Verse map stage (P1).
// Keeps camera transforms off the Blazor Server circuit so drag stays smooth.
export function attach(viewport, stage) {
  if (!viewport || !stage) return { dispose() {} };

  let scale = 0.22;
  let tx = 40;
  let ty = 40;
  let dragging = false;
  let lastX = 0;
  let lastY = 0;
  let spaceDown = false;

  const boardW = 5008;
  const boardH = 2008;

  function apply() {
    stage.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;
  }

  function fit() {
    const rect = viewport.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) return;
    const pad = 24;
    const sx = (rect.width - pad * 2) / boardW;
    const sy = (rect.height - pad * 2) / boardH;
    scale = Math.max(0.08, Math.min(sx, sy));
    tx = (rect.width - boardW * scale) / 2;
    ty = (rect.height - boardH * scale) / 2;
    apply();
  }

  function onWheel(e) {
    e.preventDefault();
    const rect = viewport.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const beforeX = (mx - tx) / scale;
    const beforeY = (my - ty) / scale;
    const factor = e.deltaY < 0 ? 1.12 : 1 / 1.12;
    scale = Math.min(3.5, Math.max(0.08, scale * factor));
    tx = mx - beforeX * scale;
    ty = my - beforeY * scale;
    apply();
  }

  function onPointerDown(e) {
    if (e.button !== 0 && e.button !== 1) return;
    // Middle mouse or Space+drag pans; otherwise left-drag also pans when not on a sector
    // (sectors stop propagation). Always allow middle / space.
    if (e.button === 1 || spaceDown || e.target === viewport || e.target === stage
        || e.target.classList?.contains("map-stage-bg")) {
      dragging = true;
      lastX = e.clientX;
      lastY = e.clientY;
      viewport.setPointerCapture(e.pointerId);
      viewport.classList.add("is-panning");
      e.preventDefault();
    }
  }

  function onPointerMove(e) {
    if (!dragging) return;
    tx += e.clientX - lastX;
    ty += e.clientY - lastY;
    lastX = e.clientX;
    lastY = e.clientY;
    apply();
  }

  function onPointerUp(e) {
    if (!dragging) return;
    dragging = false;
    viewport.classList.remove("is-panning");
    try { viewport.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
  }

  function onKeyDown(e) {
    if (e.code === "Space") spaceDown = true;
    if (e.key === "0") fit();
  }
  function onKeyUp(e) {
    if (e.code === "Space") spaceDown = false;
  }

  viewport.addEventListener("wheel", onWheel, { passive: false });
  viewport.addEventListener("pointerdown", onPointerDown);
  viewport.addEventListener("pointermove", onPointerMove);
  viewport.addEventListener("pointerup", onPointerUp);
  viewport.addEventListener("pointercancel", onPointerUp);
  window.addEventListener("keydown", onKeyDown);
  window.addEventListener("keyup", onKeyUp);

  // Defer fit until layout is ready.
  requestAnimationFrame(() => requestAnimationFrame(fit));
  window.addEventListener("resize", fit);

  apply();

  return {
    fit,
    dispose() {
      viewport.removeEventListener("wheel", onWheel);
      viewport.removeEventListener("pointerdown", onPointerDown);
      viewport.removeEventListener("pointermove", onPointerMove);
      viewport.removeEventListener("pointerup", onPointerUp);
      viewport.removeEventListener("pointercancel", onPointerUp);
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("resize", fit);
    }
  };
}
