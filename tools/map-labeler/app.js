(() => {
  "use strict";

  const state = {
    sectors: [],
    sectorById: new Map(),
    edges: [],
    neighbors: new Map(),
    faces: [],
    faceById: new Map(),
    faceNeighbors: new Map(),
    // faceId -> sectorId
    assignments: {},
    selectedFaceId: null,
    selectedSectorId: null,
    mode: "pick",
    wizardQueue: [],
    wizardIndex: 0,
    imageOffset: [4, 4],
    viewBox: [0, 0, 5008, 2008],
    layoutMeta: {},
    dirty: false,
  };

  const els = {
    stats: document.getElementById("stats"),
    palette: document.getElementById("palette"),
    overlay: document.getElementById("overlay"),
    boardImg: document.getElementById("boardImg"),
    board: document.getElementById("board"),
    detail: document.getElementById("detail"),
    paletteFilter: document.getElementById("paletteFilter"),
    zoneFilter: document.getElementById("zoneFilter"),
    search: document.getElementById("search"),
    wizardBar: document.getElementById("wizardBar"),
    wizardPrompt: document.getElementById("wizardPrompt"),
    btnPropagate: document.getElementById("btnPropagate"),
    btnClearFace: document.getElementById("btnClearFace"),
    btnSave: document.getElementById("btnSave"),
    btnReload: document.getElementById("btnReload"),
    btnWizardSkip: document.getElementById("btnWizardSkip"),
    btnWizardPrev: document.getElementById("btnWizardPrev"),
    toast: document.getElementById("toast"),
  };

  function showToast(msg, { error = false } = {}) {
    els.toast.textContent = msg;
    els.toast.classList.toggle("error", !!error);
    els.toast.classList.remove("hidden");
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => els.toast.classList.add("hidden"), 3500);
  }

  function buildNeighborMap(edges) {
    const m = new Map();
    for (const e of edges) {
      if (!m.has(e.a)) m.set(e.a, new Set());
      if (!m.has(e.b)) m.set(e.b, new Set());
      m.get(e.a).add(e.b);
      m.get(e.b).add(e.a);
    }
    return m;
  }

  function edgeKey(a, b) {
    return a < b ? `${a}|${b}` : `${b}|${a}`;
  }

  function buildFaceNeighbors(faces) {
    const edgeToFaces = new Map();
    for (const face of faces) {
      const pts = face.points;
      for (let i = 0; i < pts.length; i++) {
        const a = pts[i];
        const b = pts[(i + 1) % pts.length];
        const ka = `${a[0]},${a[1]}`;
        const kb = `${b[0]},${b[1]}`;
        const key = edgeKey(ka, kb);
        if (!edgeToFaces.has(key)) edgeToFaces.set(key, new Set());
        edgeToFaces.get(key).add(face.faceId);
      }
    }
    const nbrs = new Map(faces.map((f) => [f.faceId, new Set()]));
    for (const set of edgeToFaces.values()) {
      if (set.size < 2) continue;
      const ids = [...set];
      for (let i = 0; i < ids.length; i++) {
        for (let j = i + 1; j < ids.length; j++) {
          nbrs.get(ids[i]).add(ids[j]);
          nbrs.get(ids[j]).add(ids[i]);
        }
      }
    }
    return nbrs;
  }

  function assignedFaceOf(sectorId) {
    return Object.entries(state.assignments).find(([, sid]) => sid === sectorId)?.[0] || null;
  }

  function sectorAssigned(sectorId) {
    return assignedFaceOf(sectorId) != null;
  }

  function displayName(sector) {
    return sector.planet || sector.displayName || sector.id;
  }

  function updateStats() {
    const total = state.sectors.length;
    const labeled = Object.keys(state.assignments).length;
    const planets = state.sectors.filter((s) => s.isPlanetary);
    const planetsDone = planets.filter((s) => sectorAssigned(s.id)).length;
    state.dirty;
    els.stats.textContent =
      `${labeled} / ${total} faces labeled · planets ${planetsDone}/${planets.length}` +
      (state.dirty ? " · unsaved" : "");
  }

  function setDetail(html) {
    els.detail.innerHTML = html;
  }

  function refreshClearButton() {
    els.btnClearFace.disabled = !state.selectedFaceId || !state.assignments[state.selectedFaceId];
  }

  function assign(faceId, sectorId, { silent = false } = {}) {
    if (!faceId || !sectorId) return false;
    const existingFace = assignedFaceOf(sectorId);
    if (existingFace && existingFace !== faceId) {
      if (!silent && !confirm(`${sectorId} is already on ${existingFace}. Move it?`)) return false;
      delete state.assignments[existingFace];
    }
    const prev = state.assignments[faceId];
    if (prev && prev !== sectorId && !silent) {
      if (!confirm(`Replace ${prev} on ${faceId}?`)) return false;
    }
    state.assignments[faceId] = sectorId;
    state.dirty = true;
    return true;
  }

  function clearFace(faceId) {
    if (!faceId || !state.assignments[faceId]) return;
    delete state.assignments[faceId];
    state.dirty = true;
  }

  function svgPoint(pt) {
    const [ox, oy] = state.imageOffset;
    return [pt[0] - ox, pt[1] - oy];
  }

  function renderOverlay() {
    const svg = els.overlay;
    const [ , , vbW, vbH] = state.viewBox;
    const [ox, oy] = state.imageOffset;
    // Coordinate space matches the board image (SVG coords minus image offset).
    svg.setAttribute("viewBox", `0 0 ${vbW - ox * 0} ${vbH - oy * 0}`);
    // Better: image is 5000x2000 placed at offset; use image size.
    const imgW = 5000;
    const imgH = 2000;
    svg.setAttribute("viewBox", `0 0 ${imgW} ${imgH}`);

    const selectedSector = state.selectedFaceId
      ? state.assignments[state.selectedFaceId]
      : null;
    const logicalNeighbors = selectedSector
      ? state.neighbors.get(selectedSector) || new Set()
      : new Set();
    const neighborFaces = new Set();
    for (const sid of logicalNeighbors) {
      const fid = assignedFaceOf(sid);
      if (fid) neighborFaces.add(fid);
    }
    // Also geometric neighbors of selected face.
    if (state.selectedFaceId) {
      for (const fid of state.faceNeighbors.get(state.selectedFaceId) || []) {
        neighborFaces.add(fid);
      }
    }

    const parts = [];
    for (const face of state.faces) {
      const pts = face.points.map(svgPoint).map((p) => p.join(",")).join(" ");
      const sid = state.assignments[face.faceId];
      const sector = sid ? state.sectorById.get(sid) : null;
      const classes = ["face"];
      if (sid) classes.push("assigned");
      if (sector?.isPlanetary) classes.push("planetary-assigned");
      if (face.faceId === state.selectedFaceId) classes.push("selected");
      else if (neighborFaces.has(face.faceId)) classes.push("neighbor");
      parts.push(
        `<polygon data-face="${face.faceId}" class="${classes.join(" ")}" points="${pts}"></polygon>`
      );
      if (sid) {
        const [cx, cy] = svgPoint(face.centroid);
        const label = sector?.planet || sector?.displayName || sid.split("-").slice(-2).join("-");
        parts.push(
          `<text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="middle">${escapeXml(
            label
          )}</text>`
        );
      }
    }
    svg.innerHTML = parts.join("");
    svg.querySelectorAll("polygon").forEach((poly) => {
      poly.addEventListener("click", onFaceClick);
      poly.addEventListener("dragover", (e) => {
        e.preventDefault();
        poly.classList.add("drop-target");
      });
      poly.addEventListener("dragleave", () => poly.classList.remove("drop-target"));
      poly.addEventListener("drop", onFaceDrop);
    });
  }

  function escapeXml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function sectorMatchesFilters(sector) {
    const filter = els.paletteFilter.value;
    const zone = els.zoneFilter.value;
    const q = els.search.value.trim().toLowerCase();
    const assigned = sectorAssigned(sector.id);
    if (filter === "planetary-unassigned" && !(sector.isPlanetary && !assigned)) return false;
    if (filter === "unassigned" && assigned) return false;
    if (filter === "assigned" && !assigned) return false;
    if (zone && sector.zone !== zone) return false;
    if (q) {
      const hay = `${sector.id} ${sector.planet || ""} ${sector.displayName || ""} ${sector.zone || ""}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  }

  function renderPalette() {
    const list = state.sectors
      .filter(sectorMatchesFilters)
      .sort((a, b) => {
        if (!!a.isPlanetary !== !!b.isPlanetary) return a.isPlanetary ? -1 : 1;
        return a.id.localeCompare(b.id);
      });
    els.palette.innerHTML = "";
    for (const sector of list) {
      const chip = document.createElement("div");
      chip.className = "chip";
      if (sector.isPlanetary) chip.classList.add("planetary");
      if (sectorAssigned(sector.id)) chip.classList.add("assigned");
      if (sector.id === state.selectedSectorId) chip.classList.add("selected-id");
      chip.draggable = state.mode === "drag";
      chip.dataset.sectorId = sector.id;
      chip.innerHTML = `
        <div class="name">${escapeXml(displayName(sector))}</div>
        <div class="id">${escapeXml(sector.id)}</div>
        <div class="meta">${escapeXml(sector.zone || "")} · r${sector.ring ?? "?"} · #${sector.index ?? "?"}${
        sector.hasSupplyDeck ? " · supply" : ""
      }</div>`;
      chip.addEventListener("click", () => onSectorClick(sector.id));
      chip.addEventListener("dragstart", (e) => {
        e.dataTransfer.setData("text/plain", sector.id);
        e.dataTransfer.effectAllowed = "copy";
      });
      els.palette.appendChild(chip);
    }
  }

  function describeFace(faceId) {
    const face = state.faceById.get(faceId);
    const sid = state.assignments[faceId];
    const sector = sid ? state.sectorById.get(sid) : null;
    if (!face) return "No face selected.";
    if (!sector) {
      return `<strong>${faceId}</strong> unlabeled · centroid (${face.centroid.join(", ")})`;
    }
    const nbrs = [...(state.neighbors.get(sid) || [])].sort();
    return `<strong>${displayName(sector)}</strong> <code>${sid}</code> on <code>${faceId}</code><br/>Neighbors: ${
      nbrs.map((n) => `<code>${n}</code>`).join(", ") || "—"
    }`;
  }

  function onFaceClick(e) {
    const faceId = e.currentTarget.getAttribute("data-face");
    state.selectedFaceId = faceId;
    if (state.mode === "wizard") {
      const sid = currentWizardId();
      if (sid) {
        if (assign(faceId, sid)) {
          // Assigned id drops out of the queue; keep the same index (= next item).
          advanceWizard(0);
        }
      }
    } else if (state.selectedSectorId) {
      // Click-id-then-face works in pick and drag modes.
      assign(faceId, state.selectedSectorId);
    }
    refreshAll();
    setDetail(describeFace(faceId));
  }

  function faceIdFromPoint(clientX, clientY) {
    const stack = document.elementsFromPoint(clientX, clientY);
    for (const el of stack) {
      if (el.tagName === "polygon" && el.dataset.face) return el.dataset.face;
    }
    return null;
  }

  function onBoardDragOver(e) {
    if (!e.dataTransfer.types.includes("text/plain")) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    const faceId = faceIdFromPoint(e.clientX, e.clientY);
    els.overlay.querySelectorAll("polygon.drop-target").forEach((p) => p.classList.remove("drop-target"));
    if (faceId) {
      const poly = els.overlay.querySelector(`polygon[data-face="${faceId}"]`);
      if (poly) poly.classList.add("drop-target");
    }
  }

  function onBoardDrop(e) {
    e.preventDefault();
    const sectorId = e.dataTransfer.getData("text/plain");
    const faceId = faceIdFromPoint(e.clientX, e.clientY);
    els.overlay.querySelectorAll("polygon.drop-target").forEach((p) => p.classList.remove("drop-target"));
    if (!sectorId || !faceId) {
      showToast("Drop onto a sector face", { error: true });
      return;
    }
    if (assign(faceId, sectorId)) {
      state.selectedFaceId = faceId;
      state.selectedSectorId = sectorId;
      refreshAll();
      setDetail(describeFace(faceId));
      showToast(`Assigned ${sectorId} → ${faceId}`);
    }
  }

  function onFaceDrop(e) {
    e.preventDefault();
    e.stopPropagation();
    const faceId = e.currentTarget.getAttribute("data-face");
    const sectorId = e.dataTransfer.getData("text/plain");
    e.currentTarget.classList.remove("drop-target");
    if (assign(faceId, sectorId)) {
      state.selectedFaceId = faceId;
      state.selectedSectorId = sectorId;
      refreshAll();
      setDetail(describeFace(faceId));
      showToast(`Assigned ${sectorId} → ${faceId}`);
    }
  }

  function onSectorClick(sectorId) {
    state.selectedSectorId = sectorId;
    if (state.selectedFaceId && state.mode !== "wizard") {
      assign(state.selectedFaceId, sectorId);
    }
    const faceId = assignedFaceOf(sectorId);
    if (faceId) state.selectedFaceId = faceId;
    refreshAll();
    setDetail(
      faceId
        ? describeFace(faceId)
        : `<strong>${escapeXml(displayName(state.sectorById.get(sectorId)))}</strong> <code>${escapeXml(
            sectorId
          )}</code> selected — click a face on the board to place it.`
    );
  }

  function currentWizardId() {
    return state.wizardQueue[state.wizardIndex] || null;
  }

  function rebuildWizardQueue() {
    const planets = state.sectors
      .filter((s) => s.isPlanetary && !sectorAssigned(s.id))
      .map((s) => s.id)
      .sort();
    const rest = state.sectors
      .filter((s) => !s.isPlanetary && !sectorAssigned(s.id))
      .map((s) => s.id)
      .sort();
    state.wizardQueue = [...planets, ...rest];
    state.wizardIndex = Math.min(state.wizardIndex, Math.max(0, state.wizardQueue.length - 1));
  }

  function advanceWizard(delta) {
    rebuildWizardQueue();
    if (!state.wizardQueue.length) {
      state.wizardIndex = 0;
      updateWizardBar();
      return;
    }
    state.wizardIndex = Math.max(0, Math.min(state.wizardQueue.length - 1, state.wizardIndex + delta));
    // After assigning, queue rebuild drops current; keep index at next item (same index).
    if (delta > 0) {
      // no-op: rebuild already removed assigned id, index points at next
    }
    updateWizardBar();
  }

  function updateWizardBar() {
    const on = state.mode === "wizard";
    els.wizardBar.classList.toggle("hidden", !on);
    if (!on) return;
    rebuildWizardQueue();
    const sid = currentWizardId();
    if (!sid) {
      els.wizardPrompt.textContent = "All sector ids are assigned.";
      return;
    }
    const s = state.sectorById.get(sid);
    els.wizardPrompt.textContent = `Click the board sector for ${displayName(s)} (${sid}) · ${
      state.wizardIndex + 1
    }/${state.wizardQueue.length} remaining`;
  }

  function angleFromTopClockwise(cx, cy, x, y) {
    // 0 at top, increasing clockwise (SVG y-down).
    const dx = x - cx;
    const dy = y - cy;
    let a = Math.atan2(dx, -dy); // atan2(x, -y): 0 at top
    if (a < 0) a += Math.PI * 2;
    return a;
  }

  function propagate() {
    let added = 0;
    // Repeat a few passes so chains fill in.
    for (let pass = 0; pass < 8; pass++) {
      let passAdded = 0;
      passAdded += propagateByZoneRings();
      passAdded += propagateByAdjacencyIntersection();
      added += passAdded;
      if (!passAdded) break;
    }
    state.dirty = state.dirty || added > 0;
    refreshAll();
    setDetail(`Propagate filled <strong>${added}</strong> sector(s). Review highlighted neighbors, then save.`);
  }

  function zoneCenter(zone) {
    const labeled = state.faces.filter((f) => {
      const sid = state.assignments[f.faceId];
      return sid && state.sectorById.get(sid)?.zone === zone;
    });
    if (!labeled.length) return null;
    return [
      labeled.reduce((s, f) => s + f.centroid[0], 0) / labeled.length,
      labeled.reduce((s, f) => s + f.centroid[1], 0) / labeled.length,
    ];
  }

  function propagateByZoneRings() {
    let added = 0;
    const zones = [...new Set(state.sectors.map((s) => s.zone))];
    for (const zone of zones) {
      const center = zoneCenter(zone);
      if (!center) continue;
      const byRing = new Map();
      for (const s of state.sectors.filter((x) => x.zone === zone)) {
        if (!byRing.has(s.ring)) byRing.set(s.ring, []);
        byRing.get(s.ring).push(s);
      }
      for (const [ring, sectors] of byRing) {
        const expected = [...sectors].sort((a, b) => a.index - b.index);
        const labeledFaces = [];
        const unlabeledFaces = [];
        for (const face of state.faces) {
          const sid = state.assignments[face.faceId];
          if (sid) {
            const sec = state.sectorById.get(sid);
            if (sec?.zone === zone && sec.ring === ring) labeledFaces.push(face);
          } else {
            // Candidate unlabeled face: must be adjacent to at least one labeled face in this zone
            // OR within distance band of this ring's labeled faces.
            unlabeledFaces.push(face);
          }
        }
        if (!labeledFaces.length) continue;
        const dists = labeledFaces.map((f) => Math.hypot(f.centroid[0] - center[0], f.centroid[1] - center[1]));
        const dMin = Math.min(...dists) * 0.75;
        const dMax = Math.max(...dists) * 1.25;
        const ringCandidates = unlabeledFaces.filter((f) => {
          if (state.assignments[f.faceId]) return false;
          const d = Math.hypot(f.centroid[0] - center[0], f.centroid[1] - center[1]);
          return d >= dMin && d <= dMax;
        });
        // Include already-labeled faces of this ring for ordering.
        const allRingFaces = [
          ...labeledFaces.map((f) => ({ face: f, sid: state.assignments[f.faceId] })),
          ...ringCandidates.map((f) => ({ face: f, sid: null })),
        ];
        if (allRingFaces.length !== expected.length) continue;
        allRingFaces.sort((a, b) => {
          const aa = angleFromTopClockwise(center[0], center[1], a.face.centroid[0], a.face.centroid[1]);
          const bb = angleFromTopClockwise(center[0], center[1], b.face.centroid[0], b.face.centroid[1]);
          return aa - bb;
        });
        // Rotate ordering so labeled faces match their expected indices as well as possible.
        const rotation = bestRotation(allRingFaces, expected);
        for (let i = 0; i < expected.length; i++) {
          const item = allRingFaces[(i + rotation) % expected.length];
          const want = expected[i].id;
          if (!item.sid) {
            if (!sectorAssigned(want) && assign(item.face.faceId, want, { silent: true })) {
              added++;
            }
          }
        }
      }
    }
    return added;
  }

  function bestRotation(orderedFaces, expectedSectors) {
    let best = 0;
    let bestScore = -1;
    const n = expectedSectors.length;
    for (let rot = 0; rot < n; rot++) {
      let score = 0;
      for (let i = 0; i < n; i++) {
        const item = orderedFaces[(i + rot) % n];
        if (item.sid && item.sid === expectedSectors[i].id) score += 3;
        else if (item.sid) score -= 5;
      }
      if (score > bestScore) {
        bestScore = score;
        best = rot;
      }
    }
    return bestScore >= 0 ? best : 0;
  }

  function propagateByAdjacencyIntersection() {
    let added = 0;
    for (const sector of state.sectors) {
      if (sectorAssigned(sector.id)) continue;
      const nbrIds = [...(state.neighbors.get(sector.id) || [])];
      const labeledNbrFaces = nbrIds
        .map((id) => assignedFaceOf(id))
        .filter(Boolean);
      if (labeledNbrFaces.length < 1) continue;
      // Unlabeled faces adjacent to ALL labeled neighbor faces (or to at least 2 if many).
      const counts = new Map();
      for (const fid of labeledNbrFaces) {
        for (const n of state.faceNeighbors.get(fid) || []) {
          if (state.assignments[n]) continue;
          counts.set(n, (counts.get(n) || 0) + 1);
        }
      }
      const need = Math.min(labeledNbrFaces.length, Math.max(1, labeledNbrFaces.length >= 2 ? 2 : 1));
      const candidates = [...counts.entries()]
        .filter(([, c]) => c >= need)
        .sort((a, b) => b[1] - a[1]);
      if (candidates.length === 1) {
        if (assign(candidates[0][0], sector.id, { silent: true })) added++;
      }
    }
    return added;
  }

  function layoutPayload() {
    const assignments = { ...state.assignments };
    const faces = {};
    for (const [faceId, sectorId] of Object.entries(assignments)) {
      const face = state.faceById.get(faceId);
      if (!face) continue;
      faces[sectorId] = {
        faceId,
        centroid: face.centroid,
        points: face.points,
      };
    }
    const meta = {
      ...(state.layoutMeta || {}),
      description:
        state.layoutMeta?.description ||
        "Sector id → face geometry for map debugging / adjacency verification",
      schemaVersion: state.layoutMeta?.schemaVersion || "1.0",
      sourceSvg: state.layoutMeta?.sourceSvg || "reference/board/GameBoardClosedPath.svg",
      generatedBy: "tools/map-labeler",
      assignmentCount: Object.keys(assignments).length,
      sectorCount: state.sectors.length,
    };
    return {
      meta,
      // faceId -> sectorId (tool working set)
      assignments,
      // sectorId -> geometry (runtime-friendly; mirrors overlay polygons)
      sectors: faces,
    };
  }

  /** Prefer SectorLayout.json polygons over the raw SVG face extract. */
  function applyLayoutGeometry(layout) {
    const geos = layout?.sectors || {};
    let applied = 0;
    for (const [sectorId, geo] of Object.entries(geos)) {
      if (!geo || !Array.isArray(geo.points) || geo.points.length < 3) continue;
      let faceId = geo.faceId;
      if (!faceId) {
        faceId = Object.entries(state.assignments).find(([, sid]) => sid === sectorId)?.[0];
      }
      if (!faceId) continue;
      const face = state.faceById.get(faceId);
      if (!face) continue;
      face.points = geo.points.map((p) => [p[0], p[1]]);
      if (Array.isArray(geo.centroid) && geo.centroid.length >= 2) {
        face.centroid = [geo.centroid[0], geo.centroid[1]];
      }
      applied++;
    }
    if (applied) {
      state.faceNeighbors = buildFaceNeighbors(state.faces);
    }
    return applied;
  }

  async function saveLayout() {
    try {
      const payload = layoutPayload();
      const res = await fetch("/api/layout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      const data = await res.json();
      if (!res.ok) {
        setDetail(`Save failed: ${data.error || res.status}`);
        showToast(`Save failed: ${data.error || res.status}`, { error: true });
        return;
      }
      state.dirty = false;
      updateStats();
      setDetail(`Saved <strong>${data.assignments}</strong> assignments to <code>${data.path}</code>.`);
      showToast(`Saved ${data.assignments} assignments`);
    } catch (err) {
      setDetail(`Save failed: ${err}`);
      showToast(String(err), { error: true });
    }
  }

  function setMode(mode) {
    state.mode = mode;
    document.querySelectorAll(".mode-btn").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.mode === mode);
    });
    refreshAll();
  }

  function populateZones() {
    const zones = [...new Set(state.sectors.map((s) => s.zone).filter(Boolean))].sort();
    els.zoneFilter.innerHTML = '<option value="">Any zone</option>';
    for (const z of zones) {
      const opt = document.createElement("option");
      opt.value = z;
      opt.textContent = z;
      els.zoneFilter.appendChild(opt);
    }
  }

  function refreshAll() {
    renderOverlay();
    renderPalette();
    updateStats();
    refreshClearButton();
    updateWizardBar();
  }

  async function load() {
    const res = await fetch("/api/bootstrap");
    const data = await res.json();
    state.sectors = data.sectors.sectors || data.sectors;
    state.sectorById = new Map(state.sectors.map((s) => [s.id, s]));
    state.edges = data.adjacency.edges || [];
    state.neighbors = buildNeighborMap(state.edges);
    // Deep-copy face geometry so layout overrides don't mutate the bootstrap payload.
    state.faces = (data.faces.faces || []).map((f) => ({
      ...f,
      points: (f.points || []).map((p) => [p[0], p[1]]),
      centroid: f.centroid ? [f.centroid[0], f.centroid[1]] : f.centroid,
    }));
    state.faceById = new Map(state.faces.map((f) => [f.faceId, f]));
    state.faceNeighbors = buildFaceNeighbors(state.faces);
    state.imageOffset = data.faces.meta.imageOffset || [4, 4];
    state.viewBox = data.faces.meta.viewBox || [0, 0, 5008, 2008];
    state.layoutMeta = { ...(data.layout.meta || {}) };
    state.assignments = { ...(data.layout.assignments || {}) };
    // Also accept sector-keyed layout.
    if (!Object.keys(state.assignments).length && data.layout.sectors) {
      for (const [sid, geo] of Object.entries(data.layout.sectors)) {
        if (geo.faceId) state.assignments[geo.faceId] = sid;
      }
    }
    const layoutPolys = applyLayoutGeometry(data.layout || {});
    state.dirty = false;
    els.boardImg.src = data.paths.boardImage;
    populateZones();
    rebuildWizardQueue();
    setMode(state.mode);
    setDetail(
      `Loaded ${state.faces.length} faces and ${state.sectors.length} sector ids` +
        (layoutPolys
          ? ` · overlay using <strong>${layoutPolys}</strong> SectorLayout polygons`
          : " · overlay using raw SVG faces (no SectorLayout geometry yet)") +
        `. Click a planetary id, then click its face. Use Propagate after a few planets.`
    );
  }

  document.querySelectorAll(".mode-btn").forEach((btn) => {
    btn.addEventListener("click", () => setMode(btn.dataset.mode));
  });
  els.paletteFilter.addEventListener("change", renderPalette);
  els.zoneFilter.addEventListener("change", renderPalette);
  els.search.addEventListener("input", renderPalette);
  els.btnPropagate.addEventListener("click", propagate);
  els.btnClearFace.addEventListener("click", () => {
    clearFace(state.selectedFaceId);
    refreshAll();
    setDetail(describeFace(state.selectedFaceId));
  });
  els.btnSave.addEventListener("click", () => {
    saveLayout();
  });
  els.btnReload.addEventListener("click", () => load().catch((e) => setDetail(String(e))));
  els.btnWizardSkip.addEventListener("click", () => {
    state.wizardIndex = Math.min(state.wizardQueue.length - 1, state.wizardIndex + 1);
    updateWizardBar();
  });
  els.btnWizardPrev.addEventListener("click", () => {
    state.wizardIndex = Math.max(0, state.wizardIndex - 1);
    updateWizardBar();
  });

  // Board-level DnD so drops work even when the pointer is over labels/gaps.
  els.board.addEventListener("dragover", onBoardDragOver);
  els.board.addEventListener("drop", onBoardDrop);

  window.addEventListener("beforeunload", (e) => {
    if (state.dirty) {
      e.preventDefault();
      e.returnValue = "";
    }
  });

  load().catch((e) => {
    setDetail(`Failed to load: ${e}`);
    console.error(e);
  });
})();
