let calendar = null;
let summaryEl = null;

export function initCalendar(dotnetRef, el, canEdit) {
    const isMobile = window.innerWidth < 768;

    const now = new Date();
    const scrollHour = Math.max(0, now.getHours() - 1);
    const scrollTime = String(scrollHour).padStart(2, '0') + ':00:00';

    calendar = new FullCalendar.Calendar(el, {
        initialView: isMobile ? 'listWeek' : 'timeGridWeek',
        locale: 'pl',
        firstDay: 1,
        height: isMobile ? 'auto' : 'calc(100vh - 108px)',
        nowIndicator: true,
        scrollTime: scrollTime,
        slotMinTime: '06:00:00',
        slotMaxTime: '22:00:00',
        slotDuration: '00:30:00',
        slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        expandRows: true,
        headerToolbar: {
            left: 'prev,next today',
            center: 'title',
            right: isMobile
                ? 'listDay,listWeek'
                : 'timeGridWeek,timeGridDay,dayGridMonth'
        },
        buttonText: {
            today: 'Dziś',
            month: 'Miesiąc',
            week: 'Tydzień',
            day: 'Dzień',
            list: 'Lista'
        },
        events: async (info, success, failure) => {
            try {
                const events = await dotnetRef.invokeMethodAsync('GetEvents', info.startStr, info.endStr);
                success(events);
            } catch (e) { failure(e); }
        },
        dateClick: canEdit ? info => dotnetRef.invokeMethodAsync('OnDateClick', info.dateStr) : undefined,
        eventContent: renderEventContent,
        eventClick: info => dotnetRef.invokeMethodAsync('OnEventClick', parseInt(info.event.id)),
        eventDidMount: info => {
            info.el.style.cursor = 'pointer';
            const ep = info.event.extendedProps;
            info.el.title = `${ep.clientName} — ${ep.sessionType}`;
        },
        eventsSet: updateDaySummary
    });

    summaryEl = document.createElement('div');
    summaryEl.className = 'cal-day-summary';
    summaryEl.style.display = 'none';
    const toolbar = el.querySelector('.fc-header-toolbar');
    if (toolbar && toolbar.parentNode) {
        toolbar.parentNode.insertBefore(summaryEl, toolbar.nextSibling);
    }

    calendar.render();
}

function updateDaySummary(events) {
    if (!summaryEl) return;
    if (!events || events.length === 0) { summaryEl.style.display = 'none'; return; }

    const view = calendar.view;
    let start, end;
    if (view.type === 'timeGridDay' || view.type === 'listDay') {
        start = view.currentStart;
        end = new Date(start); end.setDate(end.getDate() + 1);
    } else {
        start = view.currentStart;
        end = view.currentEnd;
    }

    const visible = events.filter(e => {
        const s = e.start;
        return s >= start && s < end;
    });

    if (visible.length === 0) { summaryEl.style.display = 'none'; return; }

    const totalMin = visible.reduce((sum, e) => sum + (e.extendedProps.duration || 0), 0);
    const clients = new Set(visible.map(e => e.extendedProps.clientName)).size;
    const h = Math.floor(totalMin / 60);
    const m = totalMin % 60;
    const timeStr = h > 0 ? (m > 0 ? `${h}h ${m}min` : `${h}h`) : `${m}min`;

    summaryEl.innerHTML =
        `<span class="summary-pill">${visible.length} ${visible.length === 1 ? 'sesja' : visible.length < 5 ? 'sesje' : 'sesji'}</span>` +
        `<span class="summary-pill">${timeStr}</span>` +
        `<span class="summary-pill">${clients} ${clients === 1 ? 'klient' : clients < 5 ? 'klientów' : 'klientów'}</span>`;
    summaryEl.style.display = 'flex';
}

function renderEventContent(arg) {
    const { event, view } = arg;
    const ep = event.extendedProps;
    const statusCls = 'status-' + (ep.status || 'scheduled').toLowerCase();
    const isList = view.type.startsWith('list');
    const isMonth = view.type === 'dayGridMonth';

    const initials = ep.initials || '?';
    const clientName = ep.clientName || '';
    const sessionType = ep.sessionType || '';
    const duration = ep.duration ? `${ep.duration} min` : '';

    const dotCls = 'dot-' + (ep.status || 'scheduled').toLowerCase();

    if (isList) {
        return {
            html: `<div class="fc-event-card ${statusCls}" style="height:auto;padding:.4rem .6rem">
                <div class="fc-event-status-dot ${dotCls}"></div>
                <div class="fc-event-avatar">${initials}</div>
                <div class="fc-event-info">
                    <div class="fc-event-client">${clientName}</div>
                    <div class="fc-event-type">${sessionType}${duration ? ' · ' + duration : ''}</div>
                </div>
            </div>`
        };
    }

    if (isMonth) {
        return {
            html: `<div class="fc-event-card ${statusCls}">
                <div class="fc-event-avatar">${initials}</div>
                <div class="fc-event-info">
                    <div class="fc-event-client">${clientName}</div>
                </div>
            </div>`
        };
    }

    return {
        html: `<div class="fc-event-card ${statusCls}">
            <div class="fc-event-avatar">${initials}</div>
            <div class="fc-event-info">
                <div class="fc-event-client">${clientName}</div>
                <div class="fc-event-type">${sessionType}${duration ? ' · ' + duration : ''}</div>
            </div>
        </div>`
    };
}

export function refetchEvents() {
    calendar?.refetchEvents();
}

export function destroyCalendar() {
    if (summaryEl) { summaryEl.remove(); summaryEl = null; }
    calendar?.destroy();
    calendar = null;
}
