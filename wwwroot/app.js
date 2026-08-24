let socket = null;

const UI = {
    badge: document.getElementById("statusBadge"),
    statusText: document.getElementById("statusText"),
    volumeDisplay: document.getElementById("volumeDisplay"),
    slider: document.getElementById("slider"),
    timerBadge: document.getElementById("timerBadge"),
    deviceList: document.getElementById("deviceList"),
    displayList: document.getElementById("displayList"),
    sessionList: document.getElementById("sessionList"),
    displayCard: document.getElementById("displayCard"),
    brightnessContainer: document.getElementById("brightnessContainer"),
    brightnessDivider: document.getElementById("brightnessDivider"),
    brightnessSlider: document.getElementById("brightnessSlider"),
    brightnessDisplay: document.getElementById("brightnessDisplay"),
    nowPlayingCard: document.getElementById("nowPlayingCard"),
    npTitle: document.getElementById("npTitle"),
    npArtist: document.getElementById("npArtist"),
    npStatus: document.getElementById("npStatus"),
    npArt: document.getElementById("npArt"),
    timeHours: document.getElementById("timeHours"),
    timeMins: document.getElementById("timeMins"),
    timeSecs: document.getElementById("timeSecs"),
    pad: document.getElementById("touchpad"),
    sensSlider: document.getElementById("sensSlider"),
    sensValueDisplay: document.getElementById("sensValue"),
    hiddenKb: document.getElementById("hiddenKeyboard"),
    btnKb: document.getElementById("btnKeyboard"),
    leftClickBtn: document.getElementById("btnLeftClick")
};

function throttle(func, limit) {
    let lastFunc;
    let lastRan;
    return function() {
        const context = this;
        const args = arguments;
        if (!lastRan) {
            func.apply(context, args);
            lastRan = Date.now();
        } else {
            clearTimeout(lastFunc);
            lastFunc = setTimeout(function() {
                if ((Date.now() - lastRan) >= limit) {
                    func.apply(context, args);
                    lastRan = Date.now();
                }
            }, limit - (Date.now() - lastRan));
        }
    }
}

function debounce(func, wait) {
    let timeout;
    return function() {
        const context = this, args = arguments;
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(context, args), wait);
    };
}

let targetTimerMs = -1;
let targetTimerAction = "";
let timerInterval = null;

function updateTimerDisplay() {
    const powerBtns = document.querySelectorAll(".power-grid button:not(.btn-cancel)");
    const powerInputs = document.querySelectorAll(".timer-row input");
    
    if (targetTimerMs < 0) {
        UI.timerBadge.style.display = "none";
        UI.timerBadge.textContent = "";
        powerBtns.forEach(b => { b.disabled = false; b.style.opacity = "1"; });
        powerInputs.forEach(i => { i.disabled = false; i.style.opacity = "1"; });
        return;
    }
    
    powerBtns.forEach(b => { b.disabled = true; b.style.opacity = "0.4"; });
    powerInputs.forEach(i => { i.disabled = true; i.style.opacity = "0.4"; });
    
    const remaining = Math.max(0, Math.floor((targetTimerMs - Date.now()) / 1000));
    if (remaining === 0) {
        UI.timerBadge.textContent = "⏳ Executing...";
    } else {
        const h = String(Math.floor(remaining / 3600)).padStart(2, '0');
        const m = String(Math.floor((remaining % 3600) / 60)).padStart(2, '0');
        const s = String(remaining % 60).padStart(2, '0');
        const actionTitle = targetTimerAction.charAt(0).toUpperCase() + targetTimerAction.slice(1);
        UI.timerBadge.textContent = `⏳ ${actionTitle} in ${h}:${m}:${s}`;
    }
    UI.timerBadge.style.display = "inline";
}

function setConnectionStatus(connected) {
    if (connected) { UI.badge.classList.add("connected"); UI.statusText.textContent = "Online"; } 
    else { UI.badge.classList.remove("connected"); UI.statusText.textContent = "Offline"; }
}

function connect() {
    const protocol = location.protocol === "https:" ? "wss" : "ws";
    socket = new WebSocket(`${protocol}://${location.hostname}:8766`);
    socket.onopen = () => setConnectionStatus(true);
    socket.onclose = () => { setConnectionStatus(false); setTimeout(connect, 2000); };
    socket.onerror = () => { try { socket.close(); } catch (e) {} };
    socket.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);
            if (data.type === "volume") {
                const val = Number(data.value);
                UI.slider.value = val;
                UI.volumeDisplay.textContent = val + "%";
                const row = UI.slider.closest(".master-vol-row");
                if (row) {
                    if (data.muted) row.classList.add("muted");
                    else row.classList.remove("muted");
                }
            }
            else if (data.type === "sessions") renderSessions(data.sessions || []);
            else if (data.type === "devices") renderDevices(data.devices || []);
            else if (data.type === "displays") renderDisplays(data.displays || []);
            else if (data.type === "brightness") renderBrightness(data);
            else if (data.type === "nowplaying") renderNowPlaying(data);
            else if (data.type === "timer") {
                if (data.remaining >= 0) {
                    targetTimerMs = Date.now() + (data.remaining * 1000);
                } else {
                    targetTimerMs = -1;
                }
                targetTimerAction = data.action;
                updateTimerDisplay();
                if (targetTimerMs > 0 && !timerInterval) {
                    timerInterval = setInterval(updateTimerDisplay, 1000);
                } else if (targetTimerMs < 0 && timerInterval) {
                    clearInterval(timerInterval);
                    timerInterval = null;
                }
            }
        } catch (error) {
            console.error("Failed to handle WS message:", error);
        }
    };
}

function command(commandName, extraProps = {}) {
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify({ command: commandName, ...extraProps }));
}

function bindButton(id, cmd, extraProps = {}) {
    const el = document.getElementById(id);
    if (!el) return;

    let startX, startY, didScroll;

    el.addEventListener("touchstart", (e) => {
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        didScroll = false;
    }, { passive: true });

    el.addEventListener("touchmove", (e) => {
        if (Math.abs(e.touches[0].clientX - startX) > 8 || Math.abs(e.touches[0].clientY - startY) > 8) {
            didScroll = true;
        }
    }, { passive: true });

    el.addEventListener("touchend", (e) => {
        if (!didScroll) { e.preventDefault(); command(cmd, extraProps); }
    }, { passive: false });

    el.addEventListener("click", (e) => {
        if (e.pointerType !== "touch") command(cmd, extraProps);
    });
}

function bindHoldButton(id, cmd, extraProps = {}) {
    const el = document.getElementById(id);
    if (!el) return;
    let guardTimer = null, holdTimer = null, repeatTimer = null;
    let startX, startY, didScroll;

    const stopRepeat = () => {
        clearTimeout(guardTimer); clearTimeout(holdTimer); clearInterval(repeatTimer);
        guardTimer = holdTimer = repeatTimer = null;
    };
    const startRepeat = () => {
        command(cmd, extraProps);
        holdTimer = setTimeout(() => { repeatTimer = setInterval(() => command(cmd, extraProps), 150); }, 400);
    };

    el.addEventListener("touchstart", (e) => {
        startX = e.touches[0].clientX; startY = e.touches[0].clientY; didScroll = false;
        guardTimer = setTimeout(() => { if (!didScroll) startRepeat(); }, 100);
    }, { passive: true });
    el.addEventListener("touchmove", (e) => {
        if (!didScroll && (Math.abs(e.touches[0].clientX - startX) > 8 || Math.abs(e.touches[0].clientY - startY) > 8)) {
            didScroll = true; stopRepeat();
        }
    }, { passive: true });
    el.addEventListener("touchend", stopRepeat, { passive: true });
    el.addEventListener("touchcancel", stopRepeat, { passive: true });
    el.addEventListener("mousedown", startRepeat);
    el.addEventListener("mouseup", stopRepeat);
    el.addEventListener("mouseleave", stopRepeat);
}

let isLeftHeld = false;
if (UI.leftClickBtn) {
    UI.leftClickBtn.addEventListener("touchstart", (e) => {
        e.preventDefault();
        UI.leftClickBtn.classList.add("active");
        isLeftHeld = true;
        command("mouse_down", { button: "left" });
        if (navigator.vibrate) navigator.vibrate(50);
    }, { passive: false });

    UI.leftClickBtn.addEventListener("touchend", (e) => {
        e.preventDefault();
        UI.leftClickBtn.classList.remove("active");
        if (isLeftHeld) {
            isLeftHeld = false;
            command("mouse_up", { button: "left" });
        }
    }, { passive: false });

    UI.leftClickBtn.addEventListener("touchcancel", () => {
        UI.leftClickBtn.classList.remove("active");
        if (isLeftHeld) { 
            isLeftHeld = false; 
            command("mouse_up", { button: "left" }); 
        }
    }, { passive: false });
    
    UI.leftClickBtn.addEventListener("contextmenu", (e) => e.preventDefault());
}

bindButton("btnRightClick", "mouse_click", { button: "right" });
bindButton("volumeDisplay", "mute");
bindButton("btnMediaPrev", "media_previous");
bindButton("btnMediaPlay", "media_play_pause");
bindButton("btnMediaNext", "media_next");
bindHoldButton("btnWebLeft",  "web_left");
bindHoldButton("btnWebRight", "web_right");

let kbStartX, kbStartY, kbScrolled;
UI.btnKb.addEventListener("touchstart", (e) => {
    kbStartX = e.touches[0].clientX; kbStartY = e.touches[0].clientY; kbScrolled = false;
}, { passive: true });
UI.btnKb.addEventListener("touchmove", (e) => {
    if (Math.abs(e.touches[0].clientX - kbStartX) > 8 || Math.abs(e.touches[0].clientY - kbStartY) > 8) kbScrolled = true;
}, { passive: true });
UI.btnKb.addEventListener("touchend", (e) => {
    if (!kbScrolled) { e.preventDefault(); UI.hiddenKb.value = " "; UI.hiddenKb.focus(); UI.hiddenKb.click(); }
}, { passive: false });
UI.btnKb.addEventListener("click", (e) => {
    if (e.pointerType !== "touch") { UI.hiddenKb.value = " "; UI.hiddenKb.focus(); UI.hiddenKb.click(); }
});

UI.hiddenKb.addEventListener("input", (e) => {
    if (e.data) command("type_text", { text: e.data });
    else if (e.inputType === "deleteContentBackward") command("key_press", { key: "backspace" });
    UI.hiddenKb.value = " "; 
});
UI.hiddenKb.addEventListener("keydown", (e) => { if (e.key === "Enter") command("key_press", { key: "enter" }); });
UI.hiddenKb.addEventListener("blur", () => { UI.hiddenKb.value = ""; });

if (window.visualViewport) {
    let prevVpHeight = window.visualViewport.height;
    window.visualViewport.addEventListener("resize", () => {
        const h = window.visualViewport.height;
        if (h > prevVpHeight + 50 && document.activeElement instanceof HTMLInputElement) document.activeElement.blur();
        prevVpHeight = h;
    });
}

let currentSensitivity = 5.0, touchMode = 0;
let lastTouchX = null, lastTouchY = null, lastScrollY = null;
let touchStartX = null, touchStartY = null, touchStartTime = 0;
let touchWasDragged = false;

UI.sensSlider.addEventListener("input", function() {
    currentSensitivity = Number(this.value);
    UI.sensValueDisplay.textContent = currentSensitivity.toFixed(1) + "x";
});

UI.pad.addEventListener("touchstart", (e) => {
    e.preventDefault();
    if (e.targetTouches.length === 1) {
        touchMode = 1;
        lastTouchX = touchStartX = e.targetTouches[0].clientX;
        lastTouchY = touchStartY = e.targetTouches[0].clientY;
        touchStartTime = Date.now();
        touchWasDragged = false;
    } else if (e.targetTouches.length === 2) {
        touchMode = 2; lastScrollY = e.targetTouches[0].clientY; touchStartX = null;
    }
}, {passive: false});

UI.pad.addEventListener("touchmove", (e) => {
    e.preventDefault();
    if (touchMode === 1 && e.targetTouches.length >= 1) {
        const cx = e.targetTouches[0].clientX, cy = e.targetTouches[0].clientY;
        if (Math.abs(cx - touchStartX) > 5 || Math.abs(cy - touchStartY) > 5) touchWasDragged = true;
        if (lastTouchX !== null) command("mouse_move", { dx: (cx - lastTouchX) * currentSensitivity, dy: (cy - lastTouchY) * currentSensitivity });
        lastTouchX = cx; lastTouchY = cy;
    } else if (touchMode === 2 && e.targetTouches.length >= 2) {
        const cy = e.targetTouches[0].clientY;
        if (lastScrollY !== null) {
            const dy = (lastScrollY - cy) * -3.0;
            if (Math.abs(dy) > 1) { command("mouse_scroll", { dy: dy }); lastScrollY = cy; }
        }
    }
}, {passive: false});

UI.pad.addEventListener("touchend", (e) => {
    e.preventDefault();
    if (e.targetTouches.length === 0) {
        if (touchMode === 1 && touchStartX !== null) {
            if (!isLeftHeld && !touchWasDragged && (Date.now() - touchStartTime) < 400) command("mouse_click", { button: "left" });
        }
        touchMode = 0; lastTouchX = lastTouchY = lastScrollY = touchStartX = touchStartY = null;
    } else if (e.targetTouches.length === 1) {
        touchMode = 1; lastTouchX = e.targetTouches[0].clientX; lastTouchY = e.targetTouches[0].clientY; lastScrollY = null;
    }
});

const sendVolume = throttle((val) => command("set_volume", { value: val }), 50);
UI.slider.addEventListener("input", function() {
    const val = Number(this.value);
    UI.volumeDisplay.textContent = val + "%";
    const row = this.closest('.master-vol-row');
    if (val === 0) row.classList.add("muted"); else row.classList.remove("muted");
    sendVolume(val);
});

window.sendPowerCommand = function(action) {
    let h = parseInt(UI.timeHours.value) || 0;
    let m = parseInt(UI.timeMins.value) || 0;
    let s = parseInt(UI.timeSecs.value) || 0;
    command("power", { action: action, seconds: (h * 3600) + (m * 60) + s });
};

function renderDevices(devices) {
    if (!devices.length) { UI.deviceList.innerHTML = '<div class="hint">No devices</div>'; return; }

    const hint = UI.deviceList.querySelector('.hint');
    if (hint) hint.remove();

    const existingRows = {};
    Array.from(UI.deviceList.children).forEach(row => {
        const id = row.dataset.id;
        if (id) existingRows[id] = row;
    });

    const newRows = new Set();

    devices.forEach(d => {
        newRows.add(String(d.id));
        let btn = existingRows[d.id];

        if (btn) {
            btn.className = "device-btn" + (d.isDefault ? " active" : "");
            btn.querySelector(".dev-check").textContent = d.isDefault ? "✓" : "";
            btn.querySelector(".dev-name").textContent = d.name;
        } else {
            btn = document.createElement("button");
            btn.type = "button";
            btn.dataset.id = d.id;
            btn.className = "device-btn" + (d.isDefault ? " active" : "");
            btn.innerHTML = `<span class="dev-check">${d.isDefault ? "✓" : ""}</span><span class="dev-name"></span>`;
            btn.querySelector(".dev-name").textContent = d.name;
            btn.addEventListener("click", () => {
                const currentData = devices.find(x => x.id === d.id);
                if (currentData && !currentData.isDefault) command("set_device", { id: d.id });
            });
            UI.deviceList.appendChild(btn);
        }
    });

    Object.keys(existingRows).forEach(id => {
        if (!newRows.has(id)) existingRows[id].remove();
    });
}

let sessionDragging = false, pendingSessions = null;
const endSessionDrag = () => {
    if (!sessionDragging) return;
    sessionDragging = false;
    if (pendingSessions) { const p = pendingSessions; pendingSessions = null; renderSessions(p); }
};
window.addEventListener("mouseup", endSessionDrag);
window.addEventListener("touchend", endSessionDrag, { passive: true });
window.addEventListener("touchcancel", endSessionDrag, { passive: true });

function renderDisplays(displays) {
    if (!UI.displayList) return;
    UI.displayList.innerHTML = "";
    if (displays.length === 0) {
        UI.displayList.innerHTML = `<div class="hint">No monitors found</div>`;
        return;
    }

    displays.forEach(d => {
        const row = document.createElement("div");
        row.className = "device-row";
        
        const name = document.createElement("div");
        name.className = "device-name";
        name.textContent = (d.isPrimary ? "⭐ " : "") + (d.name || d.id);
        
        const ctl = document.createElement("div");
        ctl.className = "device-ctl";
        
        const label = document.createElement("label");
        label.className = "toggle-switch";
        
        const cb = document.createElement("input");
        cb.type = "checkbox";
        cb.checked = d.isActive;
        cb.addEventListener("change", (e) => {
            command("set_display", { id: d.id, active: cb.checked });
        });
        
        const slider = document.createElement("span");
        slider.className = "toggle-slider";
        
        label.appendChild(cb);
        label.appendChild(slider);
        ctl.appendChild(label);
        
        row.appendChild(name);
        row.appendChild(ctl);
        UI.displayList.appendChild(row);
    });
}

function renderSessions(sessions) {
    if (sessionDragging) { pendingSessions = sessions; return; }
    if (!sessions.length) { UI.sessionList.innerHTML = '<div class="hint">No active apps</div>'; return; }

    const hint = UI.sessionList.querySelector('.hint');
    if (hint) hint.remove();

    const existingRows = {};
    Array.from(UI.sessionList.children).forEach(row => {
        const id = row.dataset.id;
        if (id) existingRows[id] = row;
    });

    const newRows = new Set();

    sessions.forEach(s => {
        newRows.add(String(s.id));
        let row = existingRows[s.id];

        if (row) {
            row.className = "session-row" + (s.muted ? " muted" : "");
            row.querySelector('.session-name').textContent = s.name;
            const range = row.querySelector('input[type="range"]');
            const pct = row.querySelector('.session-pct');
            
            if (document.activeElement !== range) {
                range.value = s.volume;
                pct.textContent = s.volume + "%";
            }
        } else {
            row = document.createElement("div");
            row.dataset.id = s.id;
            row.className = "session-row" + (s.muted ? " muted" : "");

            const name = document.createElement("div"); name.className = "session-name"; name.textContent = s.name;
            const ctl = document.createElement("div"); ctl.className = "session-ctl";

            const range = document.createElement("input");
            range.type = "range"; range.min = 0; range.max = 100; range.value = s.volume;
            
            const pct = document.createElement("div"); pct.className = "session-pct"; pct.textContent = s.volume + "%";
            pct.addEventListener("click", () => command("session_mute", { id: s.id }));

            const sendAppVol = throttle((val) => command("set_session_volume", { id: s.id, value: val }), 60);
            range.addEventListener("input", () => {
                const v = Number(range.value);
                pct.textContent = v + "%";
                if (v === 0) row.classList.add("muted"); else row.classList.remove("muted");
                sendAppVol(v);
            });
            const startDrag = () => { sessionDragging = true; };
            range.addEventListener("touchstart", startDrag, { passive: true });
            range.addEventListener("mousedown", startDrag);
            range.addEventListener("touchend", endSessionDrag, { passive: true });
            range.addEventListener("touchcancel", endSessionDrag, { passive: true });

            ctl.appendChild(range); ctl.appendChild(pct); row.appendChild(name); row.appendChild(ctl);
            UI.sessionList.appendChild(row);
        }
    });

    Object.keys(existingRows).forEach(id => {
        if (!newRows.has(id)) existingRows[id].remove();
    });
}

let brightnessDragging = false;
function renderBrightness(data) {
    if (!data.supported) { 
        if (UI.brightnessContainer) UI.brightnessContainer.style.display = "none";
        if (UI.brightnessDivider) UI.brightnessDivider.style.display = "none";
        return; 
    }
    if (UI.brightnessContainer) UI.brightnessContainer.style.display = "flex";
    if (UI.brightnessDivider) UI.brightnessDivider.style.display = "block";
    
    if (!brightnessDragging) {
        UI.brightnessSlider.value = data.value;
        UI.brightnessDisplay.textContent = data.value + "%";
    }
}
const sendBright = throttle((val) => command("set_brightness", { value: val }), 60);
UI.brightnessSlider.addEventListener("input", function() {
    const v = Number(this.value);
    UI.brightnessDisplay.textContent = v + "%";
    sendBright(v);
});
UI.brightnessSlider.addEventListener("touchstart", () => { brightnessDragging = true; }, { passive: true });
UI.brightnessSlider.addEventListener("mousedown", () => { brightnessDragging = true; });
const endBrightDrag = () => { brightnessDragging = false; };
UI.brightnessSlider.addEventListener("touchend", endBrightDrag, { passive: true });
window.addEventListener("mouseup", endBrightDrag);
window.addEventListener("touchend", endBrightDrag, { passive: true });
window.addEventListener("touchcancel", endBrightDrag, { passive: true });

function renderNowPlaying(d) {
    if (!d.playing) { UI.nowPlayingCard.style.display = "none"; return; }
    UI.nowPlayingCard.style.display = "block";
    UI.npTitle.textContent = d.title || "Unknown";
    UI.npArtist.textContent = d.artist || "";
    UI.npStatus.textContent = d.status === "playing" ? "▶ Playing" : "⏸ Paused";
    if (d.thumb) { UI.npArt.style.backgroundImage = `url('${d.thumb}')`; UI.npArt.classList.add("has-art"); }
    else { UI.npArt.style.backgroundImage = ""; UI.npArt.classList.remove("has-art"); }
}

connect();
