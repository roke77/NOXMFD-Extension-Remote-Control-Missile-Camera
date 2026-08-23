// MISSILE CAMERA page — Missile Camera: Remote Control feed + controls.

function postCmd(cmd, args) {
  return fetch('/ext/rc-missile-camera/command', {
    method: 'POST',
    body: JSON.stringify(Object.assign({ cmd: cmd }, args || {})),
  });
}

const rcPanel   = document.getElementById('rc-panel');
const rcEmptyMsg= document.getElementById('rc-empty-msg');
const rcSurface = document.getElementById('rc-surface');
const rcImg     = document.getElementById('rc-img');
const rcReticle = document.getElementById('rc-reticle');
const rcMissile = document.getElementById('rc-missile');
const rcLink    = document.getElementById('rc-link');
const rcFormation = document.getElementById('rc-formation');
const rcThrFill = document.getElementById('rc-thr-fill');
const rcBoostBtn= document.getElementById('rc-boost');
const rcTakeBtn = document.getElementById('rc-take');
const rcReleaseBtn = document.getElementById('rc-release');
const rcFormBtn = document.getElementById('rc-form-btn');
const rcVisionBtn = document.getElementById('rc-vision');
const rcDetBtn  = document.getElementById('rc-det');
const rcThrUp   = document.getElementById('rc-thr-up');
const rcThrDown = document.getElementById('rc-thr-down');
const rcPoolEl  = document.getElementById('rc-pool');
const rcTeleSpd = document.getElementById('rc-tele-spd');
const rcTeleAlt = document.getElementById('rc-tele-alt');
const rcTeleRng = document.getElementById('rc-tele-rng');
const rcTeleFuel = document.getElementById('rc-tele-fuel');
const rcTeleMach = document.getElementById('rc-tele-mach');
const rcTeleG   = document.getElementById('rc-tele-g');
const rcTeleGuid = document.getElementById('rc-tele-guid');
const rcTeleTgtAngle = document.getElementById('rc-tele-tgtangle');
const rcTeleTti = document.getElementById('rc-tele-tti');
const rcMarkersEl = document.getElementById('rc-markers');

let state = { available: false, rcReady: false, fsActive: false, hasFrame: false, controlling: false, formation: false, pool: [] };
let poolRefreshed = false;

// ── MJPEG reconnect + stall watchdog ────────────────────────────────────────────────
let rcFeedRetryCount = 0;
let rcFeedRetryTimer = null;
let rcFeedLastFrameAt = Date.now();
const RC_FEED_STALL_MS = 4000;
const RC_FEED_URL = '/ext/rc-missile-camera/feed.mjpg';

function stopRcFeed() {
  if (rcFeedRetryTimer) {
    clearTimeout(rcFeedRetryTimer);
    rcFeedRetryTimer = null;
  }
  rcImg.removeAttribute('src');
  rcPanel.classList.remove('has-feed');
  rcMarkersEl.innerHTML = '';
}

function startRcFeed() {
  if (rcImg.getAttribute('src')) return;
  rcImg.src = RC_FEED_URL;
}

function scheduleRcFeedRetry() {
  if (rcFeedRetryTimer || document.visibilityState !== 'visible') return;
  rcFeedRetryTimer = setTimeout(function () {
    rcFeedRetryTimer = null;
    rcImg.src = RC_FEED_URL + '?r=' + (++rcFeedRetryCount);
  }, 1200);
}

function syncRcPageVisibility() {
  if (document.visibilityState === 'visible') startRcFeed();
  else stopRcFeed();
}

document.addEventListener('visibilitychange', syncRcPageVisibility);
window.addEventListener('pagehide', stopRcFeed);
syncRcPageVisibility();
rcImg.addEventListener('error', function() {
  rcPanel.classList.remove('has-feed');
  scheduleRcFeedRetry();
});
setInterval(function() {
  if (!state.available || !state.hasFrame) return;
  if (Date.now() - rcFeedLastFrameAt > RC_FEED_STALL_MS) {
    rcFeedLastFrameAt = Date.now();
    scheduleRcFeedRetry();
  }
}, 1000);

// Map Unity viewport (0..1, y=bottom) onto object-fit:contain letterbox inside rc-surface.
function imgLetterboxInSurface() {
  const sw = rcSurface.clientWidth;
  const sh = rcSurface.clientHeight;
  if (sw < 2 || sh < 2) return { x: 0, y: 0, w: sw, h: sh };
  const nw = rcImg.naturalWidth > 0 ? rcImg.naturalWidth : 16;
  const nh = rcImg.naturalHeight > 0 ? rcImg.naturalHeight : 9;
  const ia = nw / nh;
  const sa = sw / sh;
  let w, h, x, y;
  if (ia > sa) {
    w = sw;
    h = w / ia;
    x = 0;
    y = (sh - h) * 0.5;
  } else {
    h = sh;
    w = h * ia;
    x = (sw - w) * 0.5;
    y = 0;
  }
  return { x: x, y: y, w: w, h: h };
}

function overlayPx(vx, vy) {
  const box = imgLetterboxInSurface();
  return {
    left: (box.x + vx * box.w) + 'px',
    top: (box.y + (1 - vy) * box.h) + 'px'
  };
}

function placeOverlay(el, vx, vy) {
  const p = overlayPx(vx, vy);
  el.style.left = p.left;
  el.style.top = p.top;
}

// Re-seat overlays when the pane resizes or a new MJPEG frame arrives.
window.addEventListener('resize', function() {
  if (typeof state.aimX === 'number' && typeof state.aimY === 'number') {
    placeOverlay(rcReticle, state.aimX, state.aimY);
  }
});
rcImg.addEventListener('load', function() {
  rcFeedLastFrameAt = Date.now();
  rcFeedRetryCount = 0;
  if (state.markers && state.markers.length) renderMarkers(state.markers);
  if (typeof state.aimX === 'number' && typeof state.aimY === 'number') {
    placeOverlay(rcReticle, state.aimX, state.aimY);
  }
});

// ── Aim drag ────────────────────────────────────────────────────────────────────────
const AIM_DEG_PER_PX = 0.15;
const AIM_SEND_MS = 40;

let dragging = false;
let lastX = 0, lastY = 0;
let pendingYaw = 0, pendingPitch = 0;

rcSurface.addEventListener('pointerdown', function(e) {
  if (!state.controlling) return;
  dragging = true;
  lastX = e.clientX;
  lastY = e.clientY;
  rcSurface.setPointerCapture(e.pointerId);
});

rcSurface.addEventListener('pointermove', function(e) {
  if (!dragging) return;
  const dx = e.clientX - lastX;
  const dy = e.clientY - lastY;
  lastX = e.clientX;
  lastY = e.clientY;
  pendingYaw   += dx * AIM_DEG_PER_PX;
  pendingPitch += dy * AIM_DEG_PER_PX;
});

function endDrag(e) {
  if (!dragging) return;
  dragging = false;
  try { rcSurface.releasePointerCapture(e.pointerId); } catch (err) {}
}
rcSurface.addEventListener('pointerup', endDrag);
rcSurface.addEventListener('pointercancel', endDrag);
rcSurface.addEventListener('pointerleave', endDrag);

setInterval(function() {
  if (pendingYaw === 0 && pendingPitch === 0) return;
  postCmd('aim', { x: pendingYaw, y: pendingPitch }).catch(function() {});
  pendingYaw = 0;
  pendingPitch = 0;
}, AIM_SEND_MS);

// ── Buttons ─────────────────────────────────────────────────────────────────────────
rcTakeBtn.addEventListener('click', function() {
  postCmd('take', {}).catch(function() {});
});
rcReleaseBtn.addEventListener('click', function() {
  postCmd('release', {}).catch(function() {});
});
rcFormBtn.addEventListener('click', function() {
  postCmd('formation', {}).catch(function() {});
});
rcVisionBtn.addEventListener('click', function() {
  postCmd('vision-cycle', {}).catch(function() {});
});
rcThrUp.addEventListener('click', function() {
  postCmd('throttle-adjust', { v: 0.1 }).catch(function() {});
});
rcThrDown.addEventListener('click', function() {
  postCmd('throttle-adjust', { v: -0.1 }).catch(function() {});
});

rcBoostBtn.addEventListener('click', function() {
  if (!state.controlling) return;
  postCmd('boost', { on: !state.boost }).catch(function() {});
});
document.addEventListener('visibilitychange', function() {
  if (document.visibilityState !== 'visible' && state.boost) {
    postCmd('boost', { on: false }).catch(function() {});
  }
});

const DETONATE_HOLD_MS = 600;
let detonateTimer = null;
rcDetBtn.addEventListener('pointerdown', function(e) {
  e.preventDefault();
  rcDetBtn.classList.add('on');
  detonateTimer = setTimeout(function() {
    detonateTimer = null;
    postCmd('detonate', {}).catch(function() {});
  }, DETONATE_HOLD_MS);
});
function cancelDetonate() {
  rcDetBtn.classList.remove('on');
  if (detonateTimer) { clearTimeout(detonateTimer); detonateTimer = null; }
}
rcDetBtn.addEventListener('pointerup', cancelDetonate);
rcDetBtn.addEventListener('pointercancel', cancelDetonate);
rcDetBtn.addEventListener('pointerleave', cancelDetonate);

function renderPool(pool) {
  rcPoolEl.innerHTML = '';
  if (state.controlling || !state.rcReady || !pool || pool.length === 0) return;
  pool.forEach(function(name, i) {
    const item = document.createElement('div');
    item.className = 'rc-pool-item';
    item.textContent = name || ('#' + i);
    item.addEventListener('click', function() {
      postCmd('take-at', { index: i }).catch(function() {});
    });
    rcPoolEl.appendChild(item);
  });
}

function renderTele(tele) {
  const has = !!(tele && (tele.missile || tele.speed));
  rcPanel.classList.toggle('has-tele', has);

  rcVisionBtn.textContent = (tele && tele.visionMode) ? tele.visionMode.replace(/^MODE:\s*/, 'VIS ') : 'VIS';
  if (!has) {
    rcTeleSpd.textContent = '';
    rcTeleAlt.textContent = '';
    rcTeleRng.textContent = '';
    rcTeleFuel.textContent = '';
    rcTeleMach.textContent = '';
    rcTeleG.textContent = '';
    rcTeleGuid.textContent = '';
    rcTeleTgtAngle.textContent = '';
    rcTeleTti.textContent = '';
    return;
  }

  rcTeleSpd.textContent = tele.speed || '';
  rcTeleAlt.textContent = tele.alt || '';
  rcTeleRng.textContent = tele.range || '';
  rcTeleFuel.textContent = tele.fuel || '';
  rcTeleMach.textContent = tele.mach || '';
  rcTeleG.textContent = tele.g || '';
  rcTeleGuid.textContent = tele.guidance || '';
  rcTeleTgtAngle.textContent = tele.hasTarget ? (tele.tgtAngle || '') : '';

  if (tele.hasTti) {
    rcTeleTti.textContent = 'TTI ' + tele.ttiSec.toFixed(1) + 's';
  } else {
    rcTeleTti.textContent = '';
  }
}

function renderMarkers(markers) {
  rcMarkersEl.innerHTML = '';
  if (!markers || markers.length === 0) return;

  markers.forEach(function(m) {
    const el = document.createElement('div');
    el.className = 'rc-marker' + (m.sel ? ' selected' : '');
    placeOverlay(el, m.x, m.y);
    el.style.color = m.c || '#ffffff';

    if (m.n) {
      const label = document.createElement('div');
      label.className = 'rc-marker-label';
      label.textContent = m.n;
      el.appendChild(label);
    }

    rcMarkersEl.appendChild(el);
  });
}

function applyRcState(m) {
  state = m;

  rcPanel.classList.toggle('has-rc', !!m.available);
  rcPanel.classList.toggle('rc-ready', !!m.rcReady);
  rcPanel.classList.toggle('has-feed', !!m.hasFrame);

  if (!m.available) {
    rcEmptyMsg.textContent = '— NO SIGNAL —';
    poolRefreshed = false;
  } else if (!m.fsActive) {
    rcEmptyMsg.textContent = '— CAMERA NOT ACTIVE —';
  } else if (!m.rcReady) {
    rcEmptyMsg.textContent = '— PREVIEW ONLY (RC NOT READY) —';
  } else {
    rcEmptyMsg.textContent = '';
  }

  if (m.available && m.rcReady && !poolRefreshed) {
    poolRefreshed = true;
    postCmd('refresh-pool', {}).catch(function() {});
  }

  rcMissile.textContent = m.controlling ? (m.missile || '') : '';

  rcLink.textContent = m.controlling ? (m.link || '') : '';
  rcLink.classList.toggle('degraded', m.link === 'Degraded');
  rcLink.classList.toggle('lost', m.link === 'Lost');

  rcFormation.classList.toggle('active', !!m.formation);

  rcThrFill.style.height = Math.round((m.thr || 0) * 100) + '%';
  rcBoostBtn.classList.toggle('on', !!m.boost);

  rcTakeBtn.disabled = !m.rcReady || !m.fsActive || m.controlling;
  rcReleaseBtn.disabled = !m.controlling;
  rcFormBtn.disabled = !m.controlling;
  rcDetBtn.disabled = !m.controlling;

  if (typeof m.aimX === 'number' && typeof m.aimY === 'number') {
    placeOverlay(rcReticle, m.aimX, m.aimY);
  }

  if (!m.hasFrame) {
    rcMarkersEl.innerHTML = '';
  } else {
    renderMarkers(m.markers);
  }
  renderPool(m.pool);
  renderTele(m.tele);

  state.markers = m.markers;
  state.aimX = m.aimX;
  state.aimY = m.aimY;
}

window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;

  if (m.type === 'ext') {
    applyRcState(m.data || { available: false });
  } else if (m.type === 'orient') {
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
